using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FourKitesIntegration.WebhookReceiver
{
    /// <summary>
    /// Background worker that runs inside the WebhookReceiver service. Periodically scans
    /// FourKitesInboundCallbacks for unprocessed rows and tries to correlate each to an
    /// outbound transaction. On match:
    ///   • Updates the outbound transaction's Status to CONFIRMED (or REJECTED if webhook reports errors)
    ///   • Stamps FourKitesLoadId on the outbound transaction (so future webhooks correlate by ID)
    ///   • Marks the callback as Processed
    ///   • For LOAD_CREATION: stamps FourKitesLoadId on the Vector Load table
    ///
    /// Failures (DB transient errors, malformed payloads) are NOT propagated — the row stays
    /// unprocessed and will be retried next pass.
    ///
    /// Cadence: every 10 seconds by default. Adjust via WebhookCorrelator.PollIntervalSeconds in App.config.
    /// </summary>
    public sealed class WebhookCorrelator : IDisposable
    {
        private readonly string _connectionString;
        private readonly int _pollIntervalSeconds;
        private readonly int _batchSize;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _runTask;

        public WebhookCorrelator(string connectionString)
        {
            _connectionString = connectionString;
            _pollIntervalSeconds = int.TryParse(
                ConfigurationManager.AppSettings["WebhookCorrelator.PollIntervalSeconds"], out var n) ? n : 10;
            _batchSize = int.TryParse(
                ConfigurationManager.AppSettings["WebhookCorrelator.BatchSize"], out var b) ? b : 100;
        }

        public void Start()
        {
            if (_runTask != null) return;
            _runTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _runTask?.Wait(TimeSpan.FromSeconds(15)); }
            catch (AggregateException) { /* cancellation is expected */ }
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }

        // ─── Main loop ───────────────────────────────────────────────────────

        private async Task RunLoopAsync(CancellationToken ct)
        {
            LogInfo($"Correlator started. PollInterval={_pollIntervalSeconds}s, BatchSize={_batchSize}");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int processed = await ProcessBatchAsync(ct).ConfigureAwait(false);
                    if (processed > 0)
                        LogInfo($"Correlator processed {processed} callbacks this pass.");
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    LogError("Correlator pass failed: " + ex);
                }

                try { await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            LogInfo("Correlator stopped.");
        }

        // ─── One pass ────────────────────────────────────────────────────────

        private async Task<int> ProcessBatchAsync(CancellationToken ct)
        {
            int processed = 0;
            using (var cn = new SqlConnection(_connectionString))
            {
                await cn.OpenAsync(ct).ConfigureAwait(false);

                // Claim a batch of unprocessed callbacks. We use OUTPUT INSERTED to grab the rows we
                // claimed in a single round trip, which is atomic against concurrent correlator instances.
                // (We only expect one correlator instance, but defensive coding never hurt.)
                const string claimSql = @"
UPDATE TOP (@BatchSize) c
SET ProcessedUtc = SYSUTCDATETIME()
OUTPUT inserted.CallbackId, inserted.MessageType, inserted.FourKitesLoadId,
       inserted.LoadNumber, inserted.ReferenceNumbersJson, inserted.RawPayload
FROM dbo.FourKitesInboundCallbacks c
WHERE c.ProcessedUtc IS NULL;";

                var claimed = new System.Collections.Generic.List<ClaimedCallback>();
                using (var cmd = new SqlCommand(claimSql, cn))
                {
                    cmd.Parameters.AddWithValue("@BatchSize", _batchSize);
                    using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(ct).ConfigureAwait(false))
                        {
                            claimed.Add(new ClaimedCallback
                            {
                                CallbackId = reader.GetInt64(0),
                                MessageType = reader.IsDBNull(1) ? null : reader.GetString(1),
                                FourKitesLoadId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
                                LoadNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                                ReferenceNumbersJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                                RawPayload = reader.GetString(5)
                            });
                        }
                    }
                }

                foreach (var cb in claimed)
                {
                    try
                    {
                        await CorrelateAsync(cn, cb, ct).ConfigureAwait(false);
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        // Unset ProcessedUtc so the next pass retries. Log and continue.
                        LogError($"Correlation failed for CallbackId={cb.CallbackId}: " + ex);
                        await UnclaimAsync(cn, cb.CallbackId, ct).ConfigureAwait(false);
                    }
                }
            }
            return processed;
        }

        private async Task UnclaimAsync(SqlConnection cn, long callbackId, CancellationToken ct)
        {
            const string sql = @"UPDATE dbo.FourKitesInboundCallbacks SET ProcessedUtc = NULL WHERE CallbackId = @Id;";
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Id", callbackId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        // ─── Per-callback correlation ────────────────────────────────────────

        private async Task CorrelateAsync(SqlConnection cn, ClaimedCallback cb, CancellationToken ct)
        {
            // Extract the bits we need from the payload.
            var (isSuccess, errors, fkLoadIdFromBody) = ExtractWebhookDetails(cb.RawPayload);
            long? effectiveFkLoadId = cb.FourKitesLoadId ?? fkLoadIdFromBody;

            // Find the candidate outbound transaction.
            long? matchedTxId = null;

            // Pass 1: match by FourKitesLoadId. Strongest signal — only available after LOAD_CREATION
            // has stamped the ID on the transaction.
            if (effectiveFkLoadId.HasValue)
                matchedTxId = await FindByFkLoadIdAsync(cn, effectiveFkLoadId.Value, cb.MessageType, ct).ConfigureAwait(false);

            // Pass 2: match by PrimaryReference. Used for LOAD_CREATION callbacks and for any
            // status callback that arrives before LOAD_CREATION has been processed.
            if (matchedTxId == null)
            {
                var refs = ParseReferenceNumbers(cb.ReferenceNumbersJson, cb.LoadNumber);
                foreach (var r in refs)
                {
                    matchedTxId = await FindByPrimaryReferenceAsync(cn, r, cb.MessageType, ct).ConfigureAwait(false);
                    if (matchedTxId != null) break;
                }
            }

            // If we found a match, update the transaction and stamp the callback.
            if (matchedTxId.HasValue)
            {
                string newStatus = isSuccess ? "CONFIRMED" : "REJECTED";
                string errorsJson = errors;
                await UpdateOutboundTransactionAsync(cn, matchedTxId.Value, effectiveFkLoadId, errorsJson, newStatus, ct)
                    .ConfigureAwait(false);
                await LinkCallbackAsync(cn, cb.CallbackId, matchedTxId.Value, ct).ConfigureAwait(false);

                // For LOAD_CREATION, stamp the FourKitesLoadId onto Vector's Load table.
                if (string.Equals(cb.MessageType, "LOAD_CREATION", StringComparison.OrdinalIgnoreCase)
                    && effectiveFkLoadId.HasValue)
                {
                    await StampFourKitesLoadIdOnVectorLoadAsync(cn, matchedTxId.Value, effectiveFkLoadId.Value, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                // No match — likely an unsolicited callback (load created by another source, or
                // a stop event for a load we don't track yet). Leave linked transaction NULL but
                // mark callback Processed so we don't re-scan it forever.
                LogInfo($"No outbound match for CallbackId={cb.CallbackId} MessageType={cb.MessageType} " +
                        $"FkLoadId={effectiveFkLoadId} LoadNumber={cb.LoadNumber}");
            }
        }

        // ─── SQL helpers ─────────────────────────────────────────────────────

        private async Task<long?> FindByFkLoadIdAsync(SqlConnection cn, long fkLoadId, string messageType, CancellationToken ct)
        {
            // Most-recent matching transaction wins. If multiple outbound transactions are awaiting
            // the same callback type, oldest first would be more correct — but for v1, "most recent"
            // matches the typical "fire-and-confirm-quickly" pattern and avoids stale matches.
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.FourKitesOutboundTransactions
WHERE FourKitesLoadId = @FkLoadId
  AND Status IN ('ACK', 'PENDING')
  AND (ExpectedCallbackType IS NULL OR ExpectedCallbackType = 'NONE' OR ExpectedCallbackType LIKE '%' + @MessageType + '%')
ORDER BY CreatedUtc DESC;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@FkLoadId", fkLoadId);
                cmd.Parameters.AddWithValue("@MessageType", messageType ?? string.Empty);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? (long?)null : (long)result;
            }
        }

        private async Task<long?> FindByPrimaryReferenceAsync(SqlConnection cn, string reference, string messageType, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.FourKitesOutboundTransactions
WHERE PrimaryReference = @Ref
  AND Status IN ('ACK', 'PENDING')
  AND (ExpectedCallbackType IS NULL OR ExpectedCallbackType = 'NONE' OR ExpectedCallbackType LIKE '%' + @MessageType + '%')
ORDER BY CreatedUtc DESC;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@Ref", reference);
                cmd.Parameters.AddWithValue("@MessageType", messageType ?? string.Empty);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? (long?)null : (long)result;
            }
        }

        private async Task UpdateOutboundTransactionAsync(SqlConnection cn, long txId,
            long? fkLoadId, string errorsJson, string newStatus, CancellationToken ct)
        {
            const string sql = @"
UPDATE dbo.FourKitesOutboundTransactions
SET FourKitesLoadId = COALESCE(FourKitesLoadId, @FkLoadId),
    WebhookErrors = @Errors,
    Status = @Status,
    ConfirmedUtc = SYSUTCDATETIME()
WHERE TransactionId = @TxId
  AND Status IN ('ACK', 'PENDING');";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@TxId", txId);
                cmd.Parameters.AddWithValue("@FkLoadId", (object)fkLoadId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Errors", (object)errorsJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", newStatus);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        private async Task LinkCallbackAsync(SqlConnection cn, long callbackId, long txId, CancellationToken ct)
        {
            const string sql = @"
UPDATE dbo.FourKitesInboundCallbacks
SET MatchedTransactionId = @TxId
WHERE CallbackId = @CallbackId;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@TxId", txId);
                cmd.Parameters.AddWithValue("@CallbackId", callbackId);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// When LOAD_CREATION arrives and matches an outbound transaction, write the FourKitesLoadId
        /// back to Vector's Load table so Vector code can later reference it (and so subsequent webhooks
        /// have something to correlate against).
        ///
        /// Looks up VectorLoadId on the matched transaction and updates dbo.[Load].FourKitesLoadId.
        /// If your Vector load table name differs, edit the SQL below.
        /// </summary>
        private async Task StampFourKitesLoadIdOnVectorLoadAsync(SqlConnection cn, long txId, long fkLoadId, CancellationToken ct)
        {
            // Two-step: (1) read VectorLoadId from the transaction, (2) update Vector's Load table.
            // Could be a single statement, but breaking it lets us skip the Vector update gracefully
            // if the column or table doesn't exist (defensive — your team may name things differently).

            string vectorLoadId = null;
            using (var cmd = new SqlCommand(@"SELECT VectorLoadId FROM dbo.FourKitesOutboundTransactions WHERE TransactionId = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", txId);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (result != null && result != DBNull.Value)
                    vectorLoadId = (string)result;
            }

            if (string.IsNullOrEmpty(vectorLoadId)) return;

            try
            {
                // EDIT THIS SQL if your Vector Load table is named differently (e.g. [Loads], [Shipment]).
                // The schema additions in SqlMigrations/04_VectorSchemaAdditions.sql add the FourKitesLoadId column.
                const string updateSql = @"
UPDATE dbo.[Load]
SET FourKitesLoadId = @FkLoadId,
    FourKitesCreatedUtc = SYSUTCDATETIME(),
    FourKitesTrackingStatus = 'CREATED'
WHERE LoadId = @VectorLoadId;";

                using (var cmd = new SqlCommand(updateSql, cn))
                {
                    cmd.Parameters.AddWithValue("@FkLoadId", fkLoadId);
                    cmd.Parameters.AddWithValue("@VectorLoadId", vectorLoadId);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (SqlException ex) when (ex.Number == 208 || ex.Number == 207)
            {
                // 208 = Invalid object name (table doesn't exist)
                // 207 = Invalid column name
                // Don't fail the correlation — log and continue. Operator can fix the SQL later.
                LogWarning($"Could not stamp FourKitesLoadId on Vector load table: {ex.Message}. " +
                          "Edit the UPDATE statement in WebhookCorrelator.StampFourKitesLoadIdOnVectorLoadAsync.");
            }
        }

        // ─── Payload parsing ─────────────────────────────────────────────────

        /// <summary>
        /// Extract IsSuccess flag, Errors array (as JSON string), and FourKitesLoadId from the raw payload.
        /// Returns (true, null, fkLoadId) when there are no errors. Uses defensive parsing — payloads
        /// from different MessageTypes have different fields.
        /// </summary>
        private static (bool isSuccess, string errorsJson, long? fkLoadId) ExtractWebhookDetails(string rawPayload)
        {
            if (string.IsNullOrEmpty(rawPayload)) return (true, null, null);

            try
            {
                var obj = JObject.Parse(rawPayload);
                bool isSuccess = obj["IsSuccess"]?.Value<bool?>() ?? true; // assume success when field absent

                string errorsJson = null;
                var errorsToken = obj["Errors"];
                if (errorsToken != null && errorsToken.Type == JTokenType.Array && errorsToken.HasValues)
                {
                    errorsJson = errorsToken.ToString(Newtonsoft.Json.Formatting.None);
                    // Presence of non-empty Errors implies failure regardless of IsSuccess.
                    isSuccess = false;
                }

                long? fkLoadId = obj["FourKitesLoadId"]?.Value<long?>();
                return (isSuccess, errorsJson, fkLoadId);
            }
            catch
            {
                // Malformed JSON — pessimistic default (treat as not-yet-confirmed).
                return (true, null, null);
            }
        }

        /// <summary>
        /// Parse the ReferenceNumbers field from the callback's JSON. Falls back to LoadNumber
        /// if reference numbers are absent. Always returns a list (possibly empty).
        /// </summary>
        private static System.Collections.Generic.List<string> ParseReferenceNumbers(string referenceNumbersJson, string loadNumber)
        {
            var refs = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(loadNumber)) refs.Add(loadNumber);
            if (string.IsNullOrEmpty(referenceNumbersJson)) return refs;

            try
            {
                var token = JToken.Parse(referenceNumbersJson);
                if (token is JArray arr)
                {
                    foreach (var item in arr)
                    {
                        var s = item.Value<string>();
                        if (!string.IsNullOrEmpty(s) && !refs.Contains(s)) refs.Add(s);
                    }
                }
            }
            catch { /* leave list as-is */ }

            return refs;
        }

        // ─── Logging ─────────────────────────────────────────────────────────

        private static void LogInfo(string msg) => SafeWriteEvent(msg, System.Diagnostics.EventLogEntryType.Information);
        private static void LogWarning(string msg) => SafeWriteEvent(msg, System.Diagnostics.EventLogEntryType.Warning);
        private static void LogError(string msg) => SafeWriteEvent(msg, System.Diagnostics.EventLogEntryType.Error);

        private static void SafeWriteEvent(string msg, System.Diagnostics.EventLogEntryType type)
        {
            try { System.Diagnostics.EventLog.WriteEntry("FourKitesWebhookReceiver", "Correlator: " + msg, type); }
            catch { /* never throw from a logger */ }
        }

        // ─── Helper struct ───────────────────────────────────────────────────

        private class ClaimedCallback
        {
            public long CallbackId { get; set; }
            public string MessageType { get; set; }
            public long? FourKitesLoadId { get; set; }
            public string LoadNumber { get; set; }
            public string ReferenceNumbersJson { get; set; }
            public string RawPayload { get; set; }
        }
    }
}
