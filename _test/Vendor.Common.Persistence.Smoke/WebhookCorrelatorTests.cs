using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Events;

namespace Vendor.Common.Persistence.Smoke
{
    internal static class WebhookCorrelatorTests
    {
        public static void RegisterAll()
        {
            // ─── A. Single-batch processing ────────────────────────────────

            TestHarness.RunAsync("Correlator.A1. ProcessOneBatchAsync returns 0 on empty queue", async () =>
            {
                var correlator = BuildCorrelator();

                // Drain the queue first. Earlier InboundCallbackRepository tests in this
                // run inserted callbacks that were never claimed (their ProcessedUtc is NULL).
                // The correlator would pick them up otherwise. We want this test to verify
                // "correlator returns 0 when there's nothing left to process" — so drain
                // until ProcessOneBatchAsync returns 0, then assert one more call returns 0.
                int drained = 0;
                while (true)
                {
                    var n = await correlator.ProcessOneBatchAsync(CancellationToken.None);
                    if (n == 0) break;
                    drained += n;
                    if (drained > 1000) throw new Exception("drain runaway — too many rows to clear");
                }

                // Now the real assertion: a fresh call returns 0
                var processed = await correlator.ProcessOneBatchAsync(CancellationToken.None);
                TestHarness.AssertEqual(0, processed, "no rows to process after drain");

                // Reset counters so subsequent tests see a clean state
                TestFourKitesInboundProcessor.Reset();
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Correlator.A2. Match → outbound CONFIRMED + callback MATCHED + OnConfirmed fires", async () =>
            {
                // Arrange: insert an outbound transaction, then an inbound callback for the same load
                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);

                var loadId = DbHelper.NewTestLoadId("corr-a2");
                var evt = new LoadCreatedEvent { VectorLoadId = loadId, SourceSystem = "Smoke", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                var raw = $"{{\"VectorLoadId\":\"{loadId}\",\"msg\":\"LOAD_CREATION\"}}";
                var hash = Sha256(raw);
                var cbId = await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, MessageType = "LOAD_CREATION", IsSuccess = true });

                // Set the test processor to return a match pointing at our outbound txId
                TestFourKitesInboundProcessor.Reset();
                TestFourKitesInboundProcessor.Behavior = TestFourKitesInboundProcessor.BehaviorMode.ReturnMatch;
                TestFourKitesInboundProcessor.MatchTransactionId = txId;

                // Act
                var correlator = BuildCorrelator();
                var processed = await correlator.ProcessOneBatchAsync(CancellationToken.None);

                // Assert
                TestHarness.Assert(processed >= 1, $"at least 1 row processed, got {processed}");
                TestHarness.AssertEqual(1, TestFourKitesInboundProcessor.FindMatchingCallCount,
                    "FindMatchingTransactionAsync invoked once");
                TestHarness.AssertEqual(1, TestFourKitesInboundProcessor.OnConfirmedCallCount,
                    "OnConfirmedAsync invoked once");

                // Outbound row flipped to CONFIRMED
                var outboundStatus = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("CONFIRMED", outboundStatus, "outbound status");

                // Inbound row marked MATCHED with the right tx id
                var correlationStatus = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("MATCHED", correlationStatus, "inbound CorrelationStatus");

                var matchedTx = await DbHelper.ReadInboundAsync<long>(cbId, "MatchedTransactionId");
                TestHarness.AssertEqual(txId, matchedTx, "MatchedTransactionId");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Correlator.A3. Match with IsSuccess=false → outbound REJECTED", async () =>
            {
                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);

                var loadId = DbHelper.NewTestLoadId("corr-a3");
                var evt = new LoadCreatedEvent { VectorLoadId = loadId, SourceSystem = "Smoke", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                var raw = $"{{\"VectorLoadId\":\"{loadId}\",\"err\":\"validation\"}}";
                var hash = Sha256(raw);
                await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata
                    {
                        VectorLoadId = loadId,
                        MessageType = "LOAD_REJECTION",
                        IsSuccess = false,
                        ErrorsJson = "{\"errors\":[\"bad data\"]}"
                    });

                TestFourKitesInboundProcessor.Reset();
                TestFourKitesInboundProcessor.Behavior = TestFourKitesInboundProcessor.BehaviorMode.ReturnMatch;
                TestFourKitesInboundProcessor.MatchTransactionId = txId;

                var correlator = BuildCorrelator();
                await correlator.ProcessOneBatchAsync(CancellationToken.None);

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("REJECTED", status,
                    "outbound should be REJECTED when callback IsSuccess=false");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Correlator.A4. No match → callback NO_MATCH, no outbound update", async () =>
            {
                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);

                // Outbound in ACK state — should remain ACK after correlator runs
                var loadId = DbHelper.NewTestLoadId("corr-a4");
                var evt = new LoadCreatedEvent { VectorLoadId = loadId, SourceSystem = "Smoke", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));
                var statusBefore = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("ACK", statusBefore, "pre-state");

                // Insert a callback the processor will NOT match
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var cbId = await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, IsSuccess = true });

                TestFourKitesInboundProcessor.Reset();
                TestFourKitesInboundProcessor.Behavior = TestFourKitesInboundProcessor.BehaviorMode.ReturnNull;

                var correlator = BuildCorrelator();
                await correlator.ProcessOneBatchAsync(CancellationToken.None);

                // Callback marked NO_MATCH
                var corrStatus = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("NO_MATCH", corrStatus);

                // Outbound unchanged
                var statusAfter = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("ACK", statusAfter, "outbound unchanged");

                // OnConfirmedAsync NOT called when no match
                TestHarness.AssertEqual(0, TestFourKitesInboundProcessor.OnConfirmedCallCount,
                    "OnConfirmedAsync not invoked when no match");
            }).GetAwaiter().GetResult();

            // ─── B. Resilience ──────────────────────────────────────────────

            TestHarness.RunAsync("Correlator.B1. Processor throws in Find → callback NO_MATCH, loop continues", async () =>
            {
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var loadId = DbHelper.NewTestLoadId("corr-b1");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var cbId = await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, IsSuccess = true });

                TestFourKitesInboundProcessor.Reset();
                TestFourKitesInboundProcessor.Behavior = TestFourKitesInboundProcessor.BehaviorMode.ThrowInFind;

                Exception captured = null;
                var correlator = BuildCorrelator(ex => captured = ex);

                // Should NOT throw to caller
                await correlator.ProcessOneBatchAsync(CancellationToken.None);

                TestHarness.AssertNotNull(captured, "error handler was invoked");
                TestHarness.AssertContains(captured.Message, "explosion", "error message captured");

                // Callback marked NO_MATCH so we don't retry forever
                var status = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("NO_MATCH", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Correlator.B2. OnConfirmed throws → MATCHED still recorded, error reported", async () =>
            {
                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);

                var loadId = DbHelper.NewTestLoadId("corr-b2");
                var evt = new LoadCreatedEvent { VectorLoadId = loadId, SourceSystem = "Smoke", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var cbId = await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, IsSuccess = true });

                TestFourKitesInboundProcessor.Reset();
                TestFourKitesInboundProcessor.Behavior = TestFourKitesInboundProcessor.BehaviorMode.ThrowInOnConfirmed;
                TestFourKitesInboundProcessor.MatchTransactionId = txId;

                Exception captured = null;
                var correlator = BuildCorrelator(ex => captured = ex);

                await correlator.ProcessOneBatchAsync(CancellationToken.None);

                TestHarness.AssertNotNull(captured, "OnConfirmed exception was reported");

                // Correlation STILL recorded — the side effect failed but correlation succeeded
                var corrStatus = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("MATCHED", corrStatus, "MATCHED despite OnConfirmed throwing");

                var outboundStatus = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("CONFIRMED", outboundStatus, "outbound CONFIRMED");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Correlator.B3. No processor for vendor → callback NO_MATCH (defensive)", async () =>
            {
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var loadId = DbHelper.NewTestLoadId("corr-b3");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                // Use a vendor name with no registered processor
                var cbId = await inboundRepo.UpsertAsync("UnregisteredVendor", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, IsSuccess = true });

                TestFourKitesInboundProcessor.Reset();
                var correlator = BuildCorrelator();
                await correlator.ProcessOneBatchAsync(CancellationToken.None);

                // Processor never invoked (it's for "FourKites" not "UnregisteredVendor")
                TestHarness.AssertEqual(0, TestFourKitesInboundProcessor.FindMatchingCallCount,
                    "FK processor not invoked for unregistered vendor");

                var status = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("NO_MATCH", status,
                    "no processor → mark NO_MATCH so callback isn't reprocessed");
            }).GetAwaiter().GetResult();

            // ─── C. Loop lifecycle ──────────────────────────────────────────

            TestHarness.RunAsync("Correlator.C1. RunAsync exits gracefully on cancellation", async () =>
            {
                var correlator = BuildCorrelator();
                correlator.PollIntervalSeconds = 1; // short interval to make the test fast

                using (var cts = new CancellationTokenSource())
                {
                    var runTask = correlator.RunAsync(cts.Token);

                    // Let it tick at least once
                    await Task.Delay(100);

                    cts.Cancel();

                    // Should exit within a reasonable window. If it hangs, this assertion fails after timeout.
                    var completed = await Task.WhenAny(runTask, Task.Delay(3000));
                    TestHarness.Assert(completed == runTask,
                        "RunAsync should have exited within 3 seconds of cancellation");

                    // Should NOT have thrown
                    TestHarness.Assert(!runTask.IsFaulted,
                        $"RunAsync should exit cleanly, not fault. Exception: {runTask.Exception?.InnerException?.Message}");
                }
            }).GetAwaiter().GetResult();
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static WebhookCorrelator BuildCorrelator(Action<Exception> errorHandler = null)
        {
            var registry = new VendorAdapterRegistry(new IVendorAdapter[] { /* not used; correlator only uses GetInboundProcessor */ });

            // Manually register the test inbound processor via reflection.
            // (VendorAdapterRegistry doesn't expose a public Add method since production
            // loads everything from config. For tests we reach into the private field.)
            var processorField = typeof(VendorAdapterRegistry).GetField("_inboundProcessors",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = (System.Collections.Generic.Dictionary<string, IInboundEventProcessor>)processorField.GetValue(registry);
            dict["FourKites"] = new TestFourKitesInboundProcessor();

            var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);
            var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);

            return new WebhookCorrelator(
                DbHelper.ConnectionString, registry, inboundRepo, outboundRepo, errorHandler);
        }

        private static string Sha256(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
