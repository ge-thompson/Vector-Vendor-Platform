using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// Reads/writes VendorAPI_FK.VendorInboundCallbacks.
    ///
    /// Writes go through the usp_UpsertInboundCallback stored procedure (Deliverable #7),
    /// which uses MERGE to handle the dedupe-by-(VendorName, PayloadHash) UNIQUE constraint
    /// atomically. This makes concurrent duplicate webhooks race-safe without app-side locks.
    ///
    /// Reads serve the WebhookCorrelator's "claim a batch of unprocessed rows" pattern.
    ///
    /// Unlike the outbound repository, this one is NOT fail-silent on writes — webhook
    /// receipt persistence is critical. If we can't write the row, we want the controller
    /// to know so it can return an error to the vendor (who will retry). Read paths
    /// are also strict for the same reason.
    /// </summary>
    public class InboundCallbackRepository
    {
        private readonly string _connectionString;

        public InboundCallbackRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Persists an inbound webhook via usp_UpsertInboundCallback. Returns the
        /// CallbackId of the row (new or pre-existing duplicate).
        ///
        /// THIS METHOD IS NOT FAIL-SILENT. Exceptions propagate so the controller
        /// can return a non-2xx to the vendor and trigger their retry.
        /// </summary>
        public async Task<long> UpsertAsync(
            string vendorName,
            string payloadHash,
            string rawPayload,
            InboundEventMetadata metadata,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(vendorName))
                throw new ArgumentException("vendorName is required.", nameof(vendorName));
            if (string.IsNullOrWhiteSpace(payloadHash))
                throw new ArgumentException("payloadHash is required.", nameof(payloadHash));
            if (payloadHash.Length != 64)
                throw new ArgumentException("payloadHash must be 64 hex chars (SHA256).", nameof(payloadHash));

            metadata ??= new InboundEventMetadata();

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("dbo.usp_UpsertInboundCallback", cn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@VendorName",           SqlDbType.NVarChar, 50).Value  = vendorName;
                cmd.Parameters.Add("@PayloadHash",          SqlDbType.Char, 64).Value      = payloadHash;
                cmd.Parameters.Add("@RawPayload",           SqlDbType.NVarChar, -1).Value  = rawPayload ?? string.Empty;
                cmd.Parameters.Add("@MessageType",          SqlDbType.NVarChar, 50).Value  = (object)metadata.MessageType ?? DBNull.Value;
                cmd.Parameters.Add("@VendorLoadId",         SqlDbType.NVarChar, 100).Value = (object)metadata.VendorLoadId ?? DBNull.Value;
                cmd.Parameters.Add("@VectorLoadId",         SqlDbType.NVarChar, 50).Value  = (object)metadata.VectorLoadId ?? DBNull.Value;
                cmd.Parameters.Add("@ReferenceNumbersJson", SqlDbType.NVarChar, -1).Value  =
                    metadata.ReferenceNumbers != null
                        ? (object)Newtonsoft.Json.JsonConvert.SerializeObject(metadata.ReferenceNumbers)
                        : DBNull.Value;
                cmd.Parameters.Add("@IsSuccess",            SqlDbType.Bit).Value           = metadata.IsSuccess;
                cmd.Parameters.Add("@ErrorsJson",           SqlDbType.NVarChar, -1).Value  = (object)metadata.ErrorsJson ?? DBNull.Value;

                await cn.OpenAsync(ct).ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException(
                        "usp_UpsertInboundCallback returned no CallbackId — schema may be out of date.");

                return Convert.ToInt64(result);
            }
        }

        // ─── Correlator support — claim and process unprocessed rows ─────────

        /// <summary>
        /// Claims up to <paramref name="batchSize"/> unprocessed callbacks by setting
        /// ProcessedUtc = SYSUTCDATETIME() and returning the affected rows.
        ///
        /// This is the standard "OUTPUT-INSERTED claim" pattern: atomic against
        /// concurrent correlator instances. The caller is responsible for resetting
        /// ProcessedUtc back to NULL via <see cref="UnclaimAsync"/> if correlation fails
        /// (so the next pass retries).
        /// </summary>
        public async Task<List<InboundCallbackRow>> ClaimUnprocessedAsync(
            SqlConnection openConnection,
            int batchSize,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE TOP (@BatchSize) c
SET ProcessedUtc = SYSUTCDATETIME()
OUTPUT
    inserted.CallbackId, inserted.VendorName, inserted.PayloadHash,
    inserted.RawPayload, inserted.MessageType, inserted.VendorLoadId,
    inserted.VectorLoadId, inserted.ReferenceNumbersJson, inserted.IsSuccess,
    inserted.ErrorsJson, inserted.ReceivedUtc, inserted.LastSeenUtc,
    inserted.ReceiptCount, inserted.MatchedTransactionId,
    inserted.CorrelationStatus, inserted.CorrelationError
FROM dbo.VendorInboundCallbacks c
WHERE c.ProcessedUtc IS NULL;";

            var rows = new List<InboundCallbackRow>();
            using (var cmd = new SqlCommand(sql, openConnection))
            {
                cmd.Parameters.Add("@BatchSize", SqlDbType.Int).Value = batchSize;

                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(new InboundCallbackRow
                        {
                            CallbackId           = reader.GetInt64(0),
                            VendorName           = reader.GetString(1),
                            PayloadHash          = reader.GetString(2),
                            RawPayload           = reader.GetString(3),
                            MessageType          = reader.IsDBNull(4)  ? null : reader.GetString(4),
                            VendorLoadId         = reader.IsDBNull(5)  ? null : reader.GetString(5),
                            VectorLoadId         = reader.IsDBNull(6)  ? null : reader.GetString(6),
                            ReferenceNumbersJson = reader.IsDBNull(7)  ? null : reader.GetString(7),
                            IsSuccess            = reader.IsDBNull(8)  ? (bool?)null : reader.GetBoolean(8),
                            ErrorsJson           = reader.IsDBNull(9)  ? null : reader.GetString(9),
                            ReceivedUtc          = reader.GetDateTime(10),
                            LastSeenUtc          = reader.GetDateTime(11),
                            ReceiptCount         = reader.GetInt32(12),
                            // ProcessedUtc not returned — we just set it; caller knows
                            MatchedTransactionId = reader.IsDBNull(13) ? (long?)null : reader.GetInt64(13),
                            CorrelationStatus    = reader.IsDBNull(14) ? null : reader.GetString(14),
                            CorrelationError     = reader.IsDBNull(15) ? null : reader.GetString(15)
                        });
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// Releases a claimed-but-failed callback so the next correlator pass retries.
        /// Sets ProcessedUtc back to NULL.
        /// </summary>
        public async Task UnclaimAsync(
            SqlConnection openConnection,
            long callbackId,
            CancellationToken ct = default)
        {
            const string sql = @"UPDATE dbo.VendorInboundCallbacks SET ProcessedUtc = NULL WHERE CallbackId = @CallbackId;";
            using (var cmd = new SqlCommand(sql, openConnection))
            {
                cmd.Parameters.Add("@CallbackId", SqlDbType.BigInt).Value = callbackId;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Marks a successfully-correlated callback with its MatchedTransactionId
        /// and CorrelationStatus = 'MATCHED'.
        /// </summary>
        public async Task LinkCorrelatedAsync(
            SqlConnection openConnection,
            long callbackId,
            long matchedTransactionId,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE dbo.VendorInboundCallbacks
SET MatchedTransactionId = @TxId,
    CorrelationStatus    = 'MATCHED'
WHERE CallbackId = @CallbackId;";

            using (var cmd = new SqlCommand(sql, openConnection))
            {
                cmd.Parameters.Add("@CallbackId", SqlDbType.BigInt).Value = callbackId;
                cmd.Parameters.Add("@TxId",       SqlDbType.BigInt).Value = matchedTransactionId;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Marks a processed callback as having no matching outbound transaction.
        /// CorrelationStatus = 'NO_MATCH'. Not an error — just visibility for the dashboard.
        /// </summary>
        public async Task MarkNoMatchAsync(
            SqlConnection openConnection,
            long callbackId,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE dbo.VendorInboundCallbacks
SET CorrelationStatus = 'NO_MATCH'
WHERE CallbackId = @CallbackId;";

            using (var cmd = new SqlCommand(sql, openConnection))
            {
                cmd.Parameters.Add("@CallbackId", SqlDbType.BigInt).Value = callbackId;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        // ─── Cross-reference write (used by adapter side-effects) ────────────

        /// <summary>
        /// Records that a VectorLoadId has a corresponding VendorLoadId in a vendor's system.
        /// Wraps usp_RecordVendorLoadCrossReference from Deliverable #7.
        ///
        /// Called from IInboundEventProcessor.OnConfirmedAsync — for some vendors, this fires when
        /// a load-creation webhook confirms the load was created in the vendor's system.
        /// </summary>
        public async Task RecordCrossReferenceAsync(
            SqlConnection openConnection,
            string vectorLoadId,
            string vendorName,
            string vendorLoadId,
            string trackingStatus,
            CancellationToken ct = default)
        {
            using (var cmd = new SqlCommand("dbo.usp_RecordVendorLoadCrossReference", openConnection)
                                { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.Add("@VectorLoadId",   SqlDbType.NVarChar, 50).Value  = vectorLoadId;
                cmd.Parameters.Add("@VendorName",     SqlDbType.NVarChar, 50).Value  = vendorName;
                cmd.Parameters.Add("@VendorLoadId",   SqlDbType.NVarChar, 100).Value = vendorLoadId;
                cmd.Parameters.Add("@TrackingStatus", SqlDbType.NVarChar, 20).Value  = (object)trackingStatus ?? DBNull.Value;

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
