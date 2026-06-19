using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vendor.Common.Abstractions;
using Vendor.Common.Persistence;

namespace Vendor.FourKites.Webhooks
{
    /// <summary>
    /// IInboundEventProcessor for FourKites. Handles the inbound webhook side of the
    /// integration: parse correlation keys at receipt time, then match against outbound
    /// transactions on the background correlator thread.
    ///
    /// FK WEBHOOK PAYLOAD SHAPE (representative — refine as we see real callbacks):
    /// <code>
    /// {
    ///   "messageType": "LOAD_CREATION" | "STATUS_UPDATE" | ...,
    ///   "requestId": "{guid we sent on the outbound call}",
    ///   "loadNumber": "{our VectorLoadId}",
    ///   "fourKitesLoadId": "{FK's internal id, only on LOAD_CREATION confirmations}",
    ///   "isSuccess": true,
    ///   "errors": [...]   // present when isSuccess=false
    /// }
    /// </code>
    ///
    /// CORRELATION STRATEGY (in order of preference):
    ///   1. Match on (VendorName='FourKites', VendorRequestId = payload.requestId)
    ///      — most reliable: FK echoes the GUID we sent on the original call
    ///   2. Match on (VendorName='FourKites', VectorLoadId = payload.loadNumber)
    ///      AND Status in (ACK, PENDING) — fallback when FK doesn't echo requestId
    ///   3. No match — vendor sent us a callback for a load we never dispatched.
    ///      Normal and expected (out-of-band loads, test traffic, etc.)
    ///
    /// SIDE EFFECTS on confirmation:
    ///   - LOAD_CREATION messages carry FK's internal load id (fourKitesLoadId).
    ///     We persist the cross-reference so future events for the same VectorLoadId
    ///     know FK's tracking id.
    /// </summary>
    public class FourKitesWebhookProcessor : IInboundEventProcessor
    {
        public string VendorName => "FourKites";

        private readonly InboundCallbackRepository _inboundRepo;
        private readonly Action<Exception> _onError;

        /// <summary>Parameterless constructor — for registry reflection. Side-effect
        /// writes via this constructor go nowhere; not recommended.</summary>
        public FourKitesWebhookProcessor()
            : this(null, null, null)
        {
        }

        /// <summary>
        /// Registry-friendly DI-shape constructor. The third positional parameter
        /// (ClientProfileRepository) is ignored — this processor doesn't need it —
        /// but accepting it matches what the registry passes.
        /// </summary>
        public FourKitesWebhookProcessor(
            Vendor.Common.Configuration.ClientProfileRepository profileRepository,
            Action<Exception> errorHandler)
            : this(profileRepository, errorHandler, null)
        {
        }

        /// <summary>
        /// Full constructor. Production wiring uses BuildFor(...) which creates the
        /// inbound repo from the connection string. Tests inject directly.
        /// </summary>
        public FourKitesWebhookProcessor(
            Vendor.Common.Configuration.ClientProfileRepository profileRepository,
            Action<Exception> errorHandler,
            InboundCallbackRepository inboundRepoOverride)
        {
            _onError = errorHandler ?? (_ => { });
            _inboundRepo = inboundRepoOverride;
            // _inboundRepo may be null when constructed via reflection. The cross-reference
            // write path checks for null and skips gracefully.
        }

        // ─── Phase 1: parse correlation keys inline (fast, no I/O) ────────

        public InboundEventMetadata ParseAndExtract(string rawPayload)
        {
            // Contract: must never throw. Return an empty metadata if parsing fails.
            var meta = new InboundEventMetadata { IsSuccess = true };
            if (string.IsNullOrWhiteSpace(rawPayload)) return meta;

            try
            {
                var jo = JObject.Parse(rawPayload);

                // FK uses MixedCase field names per docs (MessageType, LoadNumber,
                // FourKitesLoadId, IsSuccess, Timestamp). Fall back to lowercase
                // variants to remain tolerant of test fixtures or future changes.
                meta.MessageType  = ReadFirst(jo, "MessageType", "messageType");
                meta.VectorLoadId = ReadFirst(jo, "LoadNumber", "loadNumber");
                meta.VendorLoadId = ReadFirst(jo, "FourKitesLoadId", "fourKitesLoadId", "loadId");

                // FK signals app-level outcome via IsSuccess. Default to true if absent.
                var isSuccess = jo["IsSuccess"] ?? jo["isSuccess"];
                if (isSuccess != null && isSuccess.Type == JTokenType.Boolean)
                    meta.IsSuccess = isSuccess.Value<bool>();

                // If errors present, capture verbatim for forensics
                var errors = jo["Errors"] ?? jo["errors"];
                if (errors != null && errors.Type != JTokenType.Null)
                    meta.ErrorsJson = errors.ToString(Newtonsoft.Json.Formatting.None);

                // Reference numbers — FK echoes for matching
                var refs = jo["ReferenceNumbers"] ?? jo["references"];
                if (refs != null && refs.Type == JTokenType.Array)
                {
                    meta.ReferenceNumbers = new List<string>();
                    foreach (var r in (JArray)refs)
                    {
                        // FK's ReferenceNumbers is a plain string array, not an object array
                        if (r.Type == JTokenType.String)
                        {
                            var val = r.ToString();
                            if (!string.IsNullOrEmpty(val)) meta.ReferenceNumbers.Add(val);
                        }
                        else
                        {
                            var val = r["value"]?.ToString();
                            if (!string.IsNullOrEmpty(val)) meta.ReferenceNumbers.Add(val);
                        }
                    }
                }
            }
            catch
            {
                // Parse failure: keep whatever we got. Raw payload is still persisted
                // by the controller for offline forensics.
            }

            return meta;
        }

        /// <summary>Read the first non-null token from a list of field names.</summary>
        private static string ReadFirst(JObject jo, params string[] names)
        {
            foreach (var n in names)
            {
                var v = jo[n]?.ToString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        // ─── Phase 2a: find matching outbound transaction (background) ────

        public async Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow callback,
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            if (callback == null) return null;

            // Pull the request id out of the raw payload — we may not have stored it
            // as an indexed column on the callback row.
            string requestIdInPayload = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(callback.RawPayload))
                {
                    var jo = JObject.Parse(callback.RawPayload);
                    requestIdInPayload = jo["requestId"]?.ToString();
                }
            }
            catch { /* fall through to load-number match */ }

            // Strategy 1: exact request id match (most reliable)
            if (!string.IsNullOrEmpty(requestIdInPayload))
            {
                var txByRequestId = await FindByRequestIdAsync(
                    connection, requestIdInPayload, cancellationToken).ConfigureAwait(false);
                if (txByRequestId.HasValue) return txByRequestId;
            }

            // Strategy 2: load-number match constrained to "in flight" rows
            // (Status IN ACK or PENDING). Avoids matching the wrong dispatch when
            // a load has multiple historical transactions.
            if (!string.IsNullOrWhiteSpace(callback.VectorLoadId))
            {
                var txByLoad = await FindByLoadNumberAsync(
                    connection, callback.VectorLoadId, cancellationToken).ConfigureAwait(false);
                if (txByLoad.HasValue) return txByLoad;
            }

            // No match — vendor sent us a callback for a load we didn't dispatch
            return null;
        }

        // ─── Phase 2b: vendor-specific side effects on confirmation ───────

        public async Task OnConfirmedAsync(
            InboundCallbackRow callback,
            long matchedTransactionId,
            SqlConnection connection,
            CancellationToken cancellationToken)
        {
            // FK side effect on LOAD_CREATION callbacks: persist the
            // (VectorLoadId, fourKitesLoadId) cross-reference so future events
            // can know FK's tracking id.
            if (callback == null) return;
            if (string.IsNullOrEmpty(callback.VectorLoadId)) return;
            if (string.IsNullOrEmpty(callback.VendorLoadId)) return;

            // Only run for LOAD_CREATION-type messages — other webhook types don't
            // carry a new vendor load id.
            var isCreation = string.Equals(callback.MessageType, "LOAD_CREATION",
                StringComparison.OrdinalIgnoreCase);
            if (!isCreation) return;

            // If no inbound repo was injected, skip the side effect gracefully.
            // (Happens in test paths that don't care about cross-refs.)
            if (_inboundRepo == null) return;

            try
            {
                await _inboundRepo.RecordCrossReferenceAsync(
                    connection,
                    vectorLoadId: callback.VectorLoadId,
                    vendorName: "FourKites",
                    vendorLoadId: callback.VendorLoadId,
                    trackingStatus: "CREATED",
                    ct: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _onError(ex);
                // Don't re-throw — the correlator already recorded the MATCHED status.
                // Side-effect failure is operationally significant but shouldn't unwind
                // the correlation.
            }
        }

        // ─── DB lookups ───────────────────────────────────────────────────

        private static async Task<long?> FindByRequestIdAsync(
            SqlConnection cn, string requestId, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VendorRequestId = @ReqId
ORDER BY CreatedUtc DESC;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@ReqId", SqlDbType.NVarChar, 100).Value = requestId;
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return (result == null || result == DBNull.Value) ? (long?)null : Convert.ToInt64(result);
            }
        }

        private static async Task<long?> FindByLoadNumberAsync(
            SqlConnection cn, string vectorLoadId, CancellationToken ct)
        {
            // Match the most recent ACK/PENDING transaction. If FK sends a callback
            // out of order (rare), we might match an earlier transaction — but the
            // outbound update is idempotent (only updates ACK/PENDING), so worst case
            // is we update a stale row, which doesn't break correctness.
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VectorLoadId = @LoadId
  AND Status IN ('ACK', 'PENDING')
ORDER BY CreatedUtc DESC;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@LoadId", SqlDbType.NVarChar, 50).Value = vectorLoadId;
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return (result == null || result == DBNull.Value) ? (long?)null : Convert.ToInt64(result);
            }
        }
    }
}
