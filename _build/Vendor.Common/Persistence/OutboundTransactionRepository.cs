using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Events;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// Writes to VendorAPI_FK.VendorOutboundTransactions. Called by VendorDispatcher
    /// on every dispatch — high write volume (~3,000 inserts/day at Phase 1 scale).
    ///
    /// Lifecycle:
    ///   1. InsertPendingAsync() right before calling the adapter — returns new TransactionId
    ///   2. RecordOutcomeAsync() after the adapter returns a VendorOperationResult
    ///   3. (later) WebhookCorrelator updates Status to CONFIRMED/REJECTED via separate UPDATE
    ///
    /// Other paths:
    ///   - RecordSkippedAsync(): no matching profile or adapter declined the event
    ///   - RecordDispatcherErrorAsync(): the dispatcher itself blew up (defensive)
    ///
    /// EVERY method swallows exceptions and returns gracefully — never breaks the caller.
    /// Errors are surfaced through the error handler callback so they're visible to monitoring
    /// without disrupting load flow.
    /// </summary>
    public class OutboundTransactionRepository
    {
        private readonly string _connectionString;
        private readonly Action<Exception> _onError;

        public OutboundTransactionRepository(
            string connectionString,
            Action<Exception> errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            _connectionString = connectionString;
            _onError = errorHandler ?? (_ => { });
        }

        // ─── Insert a new PENDING row, return its TransactionId ──────────────

        /// <summary>
        /// Inserts a PENDING row for an upcoming dispatch. Returns the new TransactionId
        /// so the caller can update the row when the adapter returns.
        ///
        /// Returns 0 if the insert fails (DB down, etc.). The caller should still attempt
        /// the dispatch — we'd rather have an un-audited successful dispatch than a
        /// blocked one.
        /// </summary>
        public async Task<long> InsertPendingAsync(
            VendorEvent evt,
            string vendorName,
            string shipperCode,
            CancellationToken ct = default)
        {
            if (evt == null) return 0;

            const string sql = @"
INSERT INTO dbo.VendorOutboundTransactions
    (VendorName, EventTypeName, VectorLoadId, ShipperCode, SourceSystem, Status, CreatedUtc)
OUTPUT INSERTED.TransactionId
VALUES
    (@VendorName, @EventTypeName, @VectorLoadId, @ShipperCode, @SourceSystem, 'PENDING', SYSUTCDATETIME());";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@VendorName",    SqlDbType.NVarChar, 50).Value  = vendorName ?? (object)DBNull.Value;
                    cmd.Parameters.Add("@EventTypeName", SqlDbType.NVarChar, 100).Value = evt.GetType().Name;
                    cmd.Parameters.Add("@VectorLoadId",  SqlDbType.NVarChar, 50).Value  = evt.VectorLoadId ?? (object)DBNull.Value;
                    cmd.Parameters.Add("@ShipperCode",   SqlDbType.NVarChar, 50).Value  = (object)shipperCode ?? DBNull.Value;
                    cmd.Parameters.Add("@SourceSystem",  SqlDbType.NVarChar, 50).Value  = (object)evt.SourceSystem ?? DBNull.Value;

                    await cn.OpenAsync(ct).ConfigureAwait(false);
                    var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    return (result == null || result == DBNull.Value) ? 0 : Convert.ToInt64(result);
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                return 0;
            }
        }

        // ─── Update the row with the adapter's result ────────────────────────

        /// <summary>
        /// Updates a PENDING row with the outcome of an adapter dispatch.
        /// Maps VendorOperationResult fields onto the appropriate columns and sets Status.
        ///
        /// If transactionId is 0 (insert failed earlier), this is a no-op — there's
        /// nothing to update.
        /// </summary>
        public async Task RecordOutcomeAsync(
            long transactionId,
            VendorOperationResult result,
            CancellationToken ct = default)
        {
            if (transactionId <= 0 || result == null) return;

            // Derive the new Status from the result.
            string status = DeriveStatus(result);

            const string sql = @"
UPDATE dbo.VendorOutboundTransactions
SET Status               = @Status,
    HttpStatusCode       = @HttpStatusCode,
    ErrorCategory        = @ErrorCategory,
    ErrorMessage         = @ErrorMessage,
    VendorRequestId      = @VendorRequestId,
    VendorLoadId         = @VendorLoadId,
    ExpectedCallbackType = @ExpectedCallbackType,
    RequestPayload       = @RequestPayload,
    ResponseBody         = @ResponseBody,
    AckUtc               = CASE WHEN @Status IN ('ACK', 'CONFIRMED') THEN SYSUTCDATETIME() ELSE AckUtc END,
    DurationMs           = @DurationMs
WHERE TransactionId = @TransactionId;";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@TransactionId",        SqlDbType.BigInt).Value       = transactionId;
                    cmd.Parameters.Add("@Status",               SqlDbType.NVarChar, 20).Value = status;
                    cmd.Parameters.Add("@HttpStatusCode",       SqlDbType.Int).Value          = (object)result.HttpStatusCode ?? DBNull.Value;
                    cmd.Parameters.Add("@ErrorCategory",        SqlDbType.NVarChar, 20).Value = (object)result.ErrorCategory ?? DBNull.Value;
                    cmd.Parameters.Add("@ErrorMessage",         SqlDbType.NVarChar, -1).Value = (object)result.ErrorMessage ?? DBNull.Value;
                    cmd.Parameters.Add("@VendorRequestId",      SqlDbType.NVarChar, 100).Value = (object)result.VendorRequestId ?? DBNull.Value;
                    cmd.Parameters.Add("@VendorLoadId",         SqlDbType.NVarChar, 100).Value = (object)result.VendorLoadId ?? DBNull.Value;
                    cmd.Parameters.Add("@ExpectedCallbackType", SqlDbType.NVarChar, 50).Value  = (object)result.ExpectedCallbackType ?? DBNull.Value;
                    cmd.Parameters.Add("@RequestPayload",       SqlDbType.NVarChar, -1).Value  = (object)result.RequestPayloadJson ?? DBNull.Value;
                    cmd.Parameters.Add("@ResponseBody",         SqlDbType.NVarChar, -1).Value  = (object)result.ResponseBodyJson ?? DBNull.Value;
                    cmd.Parameters.Add("@DurationMs",           SqlDbType.Int).Value           =
                        result.Duration.TotalMilliseconds > 0
                            ? (object)(int)result.Duration.TotalMilliseconds
                            : DBNull.Value;

                    await cn.OpenAsync(ct).ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        // ─── "Skipped" path: no matching profile, or adapter declined ────────

        /// <summary>
        /// Records a SKIPPED transaction — the dispatcher had an event but no
        /// matching active profile, or the adapter's CanHandle returned false.
        /// Logged so we can see "events with no destination" on the dashboard.
        /// </summary>
        public async Task InsertSkippedAsync(
            VendorEvent evt,
            string vendorName,
            string reason,
            CancellationToken ct = default)
        {
            if (evt == null) return;

            const string sql = @"
INSERT INTO dbo.VendorOutboundTransactions
    (VendorName, EventTypeName, VectorLoadId, SourceSystem, Status,
     ErrorCategory, ErrorMessage, CreatedUtc)
VALUES
    (@VendorName, @EventTypeName, @VectorLoadId, @SourceSystem, 'SKIPPED',
     'Skipped', @Reason, SYSUTCDATETIME());";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@VendorName",    SqlDbType.NVarChar, 50).Value  = (object)vendorName ?? DBNull.Value;
                    cmd.Parameters.Add("@EventTypeName", SqlDbType.NVarChar, 100).Value = evt.GetType().Name;
                    cmd.Parameters.Add("@VectorLoadId",  SqlDbType.NVarChar, 50).Value  = (object)evt.VectorLoadId ?? DBNull.Value;
                    cmd.Parameters.Add("@SourceSystem",  SqlDbType.NVarChar, 50).Value  = (object)evt.SourceSystem ?? DBNull.Value;
                    cmd.Parameters.Add("@Reason",        SqlDbType.NVarChar, -1).Value  = (object)reason ?? DBNull.Value;

                    await cn.OpenAsync(ct).ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        // ─── Dispatcher-level error (defensive — should be rare) ─────────────

        /// <summary>
        /// Records a dispatcher-level error. Differs from a normal failed dispatch in
        /// that the failure happened in the framework itself (e.g., shipper lookup
        /// threw, registry lookup threw). Logged as DEAD_LETTER for visibility.
        /// </summary>
        public async Task InsertDispatcherErrorAsync(
            VendorEvent evt,
            Exception ex,
            CancellationToken ct = default)
        {
            if (evt == null) return;

            const string sql = @"
INSERT INTO dbo.VendorOutboundTransactions
    (VendorName, EventTypeName, VectorLoadId, SourceSystem, Status,
     ErrorCategory, ErrorMessage, CreatedUtc)
VALUES
    ('(dispatcher)', @EventTypeName, @VectorLoadId, @SourceSystem, 'DEAD_LETTER',
     'Unknown', @ErrorMessage, SYSUTCDATETIME());";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@EventTypeName", SqlDbType.NVarChar, 100).Value = evt.GetType().Name;
                    cmd.Parameters.Add("@VectorLoadId",  SqlDbType.NVarChar, 50).Value  = (object)evt.VectorLoadId ?? DBNull.Value;
                    cmd.Parameters.Add("@SourceSystem",  SqlDbType.NVarChar, 50).Value  = (object)evt.SourceSystem ?? DBNull.Value;
                    cmd.Parameters.Add("@ErrorMessage",  SqlDbType.NVarChar, -1).Value  = (object)ex?.ToString() ?? DBNull.Value;

                    await cn.OpenAsync(ct).ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception innerEx)
            {
                _onError(innerEx);
            }
        }

        // ─── Used by WebhookCorrelator to flip Status to CONFIRMED / REJECTED ─

        /// <summary>
        /// Updates an outbound transaction's Status when an inbound webhook correlates.
        /// Called by WebhookCorrelator after IInboundEventProcessor.FindMatchingTransactionAsync
        /// finds a match.
        ///
        /// Only updates if the row is currently in ACK or PENDING state — avoids
        /// races where a later webhook overwrites a state set by a still-later one.
        /// </summary>
        public async Task UpdateStatusFromWebhookAsync(
            long transactionId,
            string newStatus,                    // "CONFIRMED" or "REJECTED"
            string vendorLoadId,
            string webhookErrorsJson,
            CancellationToken ct = default)
        {
            if (transactionId <= 0) return;

            const string sql = @"
UPDATE dbo.VendorOutboundTransactions
SET Status        = @Status,
    VendorLoadId  = COALESCE(VendorLoadId, @VendorLoadId),
    ResponseBody  = COALESCE(@WebhookErrors, ResponseBody),
    ConfirmedUtc  = SYSUTCDATETIME()
WHERE TransactionId = @TransactionId
  AND Status IN ('ACK', 'PENDING');";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.Add("@TransactionId", SqlDbType.BigInt).Value       = transactionId;
                    cmd.Parameters.Add("@Status",        SqlDbType.NVarChar, 20).Value = newStatus;
                    cmd.Parameters.Add("@VendorLoadId",  SqlDbType.NVarChar, 100).Value = (object)vendorLoadId ?? DBNull.Value;
                    cmd.Parameters.Add("@WebhookErrors", SqlDbType.NVarChar, -1).Value = (object)webhookErrorsJson ?? DBNull.Value;

                    await cn.OpenAsync(ct).ConfigureAwait(false);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }

        // ─── Status derivation from a VendorOperationResult ──────────────────

        /// <summary>
        /// Maps a VendorOperationResult to the appropriate Status column value.
        /// Public + static so the dispatcher and tests can use the same logic.
        /// </summary>
        public static string DeriveStatus(VendorOperationResult result)
        {
            if (result == null) return "DEAD_LETTER";
            if (result.Success) return "ACK";  // CONFIRMED comes later via webhook

            // Map ErrorCategory to Status. Defensive defaults.
            switch (result.ErrorCategory)
            {
                case "Transient":  return "TRANSPORT_FAIL";
                case "Permanent":  return "HTTP_FAIL";
                case "RateLimit":  return "RATE_LIMITED";
                case "Skipped":    return "SKIPPED";
                default:
                    // If HTTP status code looks like a synchronous 4xx, treat as HTTP_FAIL;
                    // otherwise TRANSPORT_FAIL (network-level).
                    return result.HttpStatusCode.HasValue ? "HTTP_FAIL" : "TRANSPORT_FAIL";
            }
        }
    }
}
