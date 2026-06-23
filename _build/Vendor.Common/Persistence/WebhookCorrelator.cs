using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Dispatch;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// Background worker that matches inbound webhook callbacks to outbound
    /// transactions. The inbound counterpart to VendorDispatcher.
    ///
    /// HOSTING: started from the hosting app's startup (OTR API Global.asax
    /// Application_Start, FBS service OnStart, etc.) and stopped at shutdown
    /// via the provided CancellationToken.
    ///
    /// PROCESSING LOOP (every PollIntervalSeconds):
    ///   1. Claim a batch of unprocessed VendorInboundCallbacks rows (atomic via OUTPUT-INSERTED)
    ///   2. For each row:
    ///      - Look up the IInboundEventProcessor for the vendor
    ///      - Call FindMatchingTransactionAsync — returns matched TransactionId or null
    ///      - If matched: flip outbound row to CONFIRMED/REJECTED, call OnConfirmedAsync,
    ///        mark callback MATCHED
    ///      - If not matched: mark callback NO_MATCH (not an error — vendor sometimes
    ///        sends webhooks for loads we never dispatched)
    ///   3. Sleep, repeat
    ///
    /// THREE RULES THE LOOP HONORS:
    ///   1. Per-callback failures don't break the loop — caught + recorded; continue
    ///   2. Per-batch failures don't crash the worker — caught + logged; next tick retries
    ///   3. Graceful shutdown via CancellationToken — checked between batches
    ///
    /// CONNECTION SHARING: one SqlConnection is opened per batch and reused across
    /// all repository calls for callbacks in that batch. Avoids connection-pool thrash
    /// at scale (the alternative — one connection per repository call — would open
    /// ~5x as many connections per minute).
    /// </summary>
    public class WebhookCorrelator
    {
        private readonly string _connectionString;
        private readonly VendorAdapterRegistry _registry;
        private readonly InboundCallbackRepository _inboundRepo;
        private readonly OutboundTransactionRepository _outboundRepo;
        private readonly Action<Exception> _onError;

        /// <summary>How often the loop wakes up to claim a batch. Default 10 seconds.</summary>
        public int PollIntervalSeconds { get; set; } = 10;

        /// <summary>Maximum rows claimed per tick. Default 50.</summary>
        public int BatchSize { get; set; } = 50;

        public WebhookCorrelator(
            string connectionString,
            VendorAdapterRegistry registry,
            InboundCallbackRepository inboundRepo,
            OutboundTransactionRepository outboundRepo,
            Action<Exception> errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            _connectionString = connectionString;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _inboundRepo = inboundRepo ?? throw new ArgumentNullException(nameof(inboundRepo));
            _outboundRepo = outboundRepo ?? throw new ArgumentNullException(nameof(outboundRepo));
            _onError = errorHandler ?? (_ => { });
        }

        /// <summary>
        /// The long-running loop. Hosting code calls this once on a background Task
        /// and lets it run for the app's lifetime. Returns gracefully when the token
        /// is cancelled.
        ///
        /// Example hosting code:
        ///   var cts = new CancellationTokenSource();
        ///   _ = Task.Run(() => correlator.RunAsync(cts.Token));
        ///   // On shutdown: cts.Cancel();
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOneBatchAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown — exit the loop cleanly
                    return;
                }
                catch (Exception ex)
                {
                    // Per-batch safety net. Log and continue — the next tick will retry.
                    _onError(ex);
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(PollIntervalSeconds),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Processes exactly one batch. Public so tests can drive it deterministically
        /// (avoid waiting through Task.Delay between ticks). Returns the number of
        /// callbacks processed (matched + no-match combined).
        /// </summary>
        public async Task<int> ProcessOneBatchAsync(CancellationToken cancellationToken)
        {
            using (var cn = new SqlConnection(_connectionString))
            {
                await cn.OpenAsync(cancellationToken).ConfigureAwait(false);

                var batch = await _inboundRepo.ClaimUnprocessedAsync(cn, BatchSize, cancellationToken)
                                              .ConfigureAwait(false);

                if (batch.Count == 0) return 0;

                int processed = 0;
                foreach (var callback in batch)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        await ProcessOneCallbackAsync(cn, callback, cancellationToken)
                            .ConfigureAwait(false);
                        processed++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Per-callback safety net. Release the claim so a future tick retries.
                        _onError(ex);
                        try
                        {
                            await _inboundRepo.UnclaimAsync(cn, callback.CallbackId, cancellationToken)
                                              .ConfigureAwait(false);
                        }
                        catch { /* best effort */ }
                    }
                }

                return processed;
            }
        }

        // ─── Per-callback processing ─────────────────────────────────────────

        private async Task ProcessOneCallbackAsync(
            SqlConnection cn, InboundCallbackRow callback, CancellationToken ct)
        {
            // 1. Find the vendor's processor. If we don't have one (config mismatch,
            // unknown vendor), mark NO_MATCH so the callback isn't reprocessed forever.
            var processor = _registry.GetInboundProcessor(callback.VendorName);
            if (processor == null)
            {
                await _inboundRepo.MarkNoMatchAsync(cn, callback.CallbackId, ct).ConfigureAwait(false);
                return;
            }

            // 2. Ask the processor to find the matching outbound transaction
            long? matchedTxId;
            try
            {
                matchedTxId = await processor.FindMatchingTransactionAsync(callback, cn, ct)
                                             .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Processor violated contract (should never throw). Log and treat as no-match
                // so we don't get stuck retrying forever.
                _onError(ex);
                await _inboundRepo.MarkNoMatchAsync(cn, callback.CallbackId, ct).ConfigureAwait(false);
                return;
            }

            // 3a. No match — vendor sent us a callback for a load we didn't dispatch.
            // Normal and expected; mark NO_MATCH and move on.
            if (!matchedTxId.HasValue)
            {
                await _inboundRepo.MarkNoMatchAsync(cn, callback.CallbackId, ct).ConfigureAwait(false);
                return;
            }

            // 3b. Match found. Determine outbound status from callback.IsSuccess.
            // True (or null = treat as success) → CONFIRMED.
            // False → REJECTED.
            var newStatus = (callback.IsSuccess.HasValue && !callback.IsSuccess.Value)
                ? "REJECTED" : "CONFIRMED";

            // 4. Update the outbound transaction (only flips ACK/PENDING → new status; idempotent)
            await _outboundRepo.UpdateStatusFromWebhookAsync(
                matchedTxId.Value,
                newStatus,
                callback.VendorLoadId,
                callback.ErrorsJson,
                ct).ConfigureAwait(false);

            // 5. Link the callback to the matched transaction
            await _inboundRepo.LinkCorrelatedAsync(cn, callback.CallbackId, matchedTxId.Value, ct)
                              .ConfigureAwait(false);

            // 6. Let the vendor's processor do its side effects (e.g., stamping the
            // vendor's load id on Vector's Load table). Per the contract,
            // OnConfirmedAsync should not throw — but defensive catch anyway so a
            // side-effect failure doesn't unwind the correlation we just recorded.
            try
            {
                await processor.OnConfirmedAsync(callback, matchedTxId.Value, cn, ct)
                               .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _onError(ex);
                // We deliberately do NOT unclaim or revert here. The correlation
                // happened; the side effect failed. Operations can re-run the side
                // effect manually if needed; the audit trail shows MATCHED.
            }
        }
    }
}
