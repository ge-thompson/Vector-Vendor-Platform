using System;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Events;
using Vendor.Common.Persistence;

namespace Vendor.Common.Dispatch.Smoke
{
    internal static class DispatchSadPathTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("D1. No matching profile → SKIPPED row, adapter NOT called", async () =>
            {
                DispatchHappyPathTests.SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("d1");
                // Use an event type the seed FK profile does NOT enable.
                // (Seed enables: LoadCreatedEvent, LoadAssignedEvent, LocationReportedEvent,
                //  LoadStatusEvent, LoadTrackingStoppedEvent, DocumentAvailableEvent.)
                // GenericLoadEvent is NOT in that list — perfect for this test.
                var evt = new GenericLoadEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    EventName = "SomethingObscure"
                };

                VendorDispatcher.Instance.Dispatch(evt);

                TestHarness.AssertEqual(0, TestFourKitesAdapter.DispatchCallCount,
                    "adapter should NOT be dispatched when no profile matches");

                var rowCount = await DbHelper.CountOutboundRowsAsync(loadId);
                TestHarness.AssertEqual(1, rowCount, "one SKIPPED row should be audited");

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("SKIPPED", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("D2. Adapter declines via CanHandle → SKIPPED, adapter NOT dispatched", async () =>
            {
                DispatchHappyPathTests.SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.DeclineViaCanHandle;

                var loadId = DbHelper.NewTestLoadId("d2");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                TestHarness.Assert(TestFourKitesAdapter.CanHandleCallCount > 0,
                    "CanHandle should have been called");
                TestHarness.AssertEqual(0, TestFourKitesAdapter.DispatchCallCount,
                    "DispatchAsync should NOT be called when CanHandle returns false");

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("SKIPPED", status);

                var errCategory = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorCategory");
                TestHarness.AssertEqual("Skipped", errCategory);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("D3. Adapter throws → caught, audited as failure, no propagation", async () =>
            {
                DispatchHappyPathTests.SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ThrowInDispatch;

                var loadId = DbHelper.NewTestLoadId("d3");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                // The dispatcher's contract: NEVER throws. Calling Dispatch should be safe
                // even when the adapter explodes.
                Exception caught = null;
                try { VendorDispatcher.Instance.Dispatch(evt); }
                catch (Exception ex) { caught = ex; }

                TestHarness.AssertNull(caught,
                    "VendorDispatcher.Dispatch must NEVER throw, even when adapter throws");

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                TestHarness.Assert(txId > 0, "should have a transaction row even on failure");

                // The dispatcher catches via Failed(Exception, "Unknown") → DeriveStatus falls through
                // to: HTTP code missing → TRANSPORT_FAIL.
                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.Assert(
                    status == "TRANSPORT_FAIL" || status == "HTTP_FAIL" || status == "DEAD_LETTER",
                    $"expected a failure status, got {status}");

                var errMsg = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorMessage");
                TestHarness.AssertContains(errMsg, "simulated adapter explosion", "ErrorMessage carries the exception");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("D4. Adapter returns null → dispatcher synthesizes failure", async () =>
            {
                DispatchHappyPathTests.SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnNull;

                var loadId = DbHelper.NewTestLoadId("d4");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                TestHarness.Assert(txId > 0, "audit row exists despite null result");

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.Assert(status != "ACK", $"status should NOT be ACK on null result; got {status}");

                var errMsg = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorMessage");
                TestHarness.AssertContains(errMsg, "null", "ErrorMessage explains the null-return");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("D5. Adapter returns HTTP_FAIL → status mapped correctly", async () =>
            {
                DispatchHappyPathTests.SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnFailHttp;

                var loadId = DbHelper.NewTestLoadId("d5");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("HTTP_FAIL", status);

                var http = await DbHelper.ReadOutboundAsync<int>(txId, "HttpStatusCode");
                TestHarness.AssertEqual(400, http);

                var errCategory = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorCategory");
                TestHarness.AssertEqual("Permanent", errCategory);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("D6. Profile points at unregistered vendor → SKIPPED row", async () =>
            {
                // Build a registry with NO adapters. The seed profile points at "FourKites",
                // but the registry has no FK adapter — dispatcher should audit SKIPPED.
                VendorDispatcher.ResetForTesting();
                TestFourKitesAdapter.Reset();

                var profileRepo = new ClientProfileRepository(DbHelper.ConnectionString);
                var auditRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var emptyRegistry = new VendorAdapterRegistry(new IVendorAdapter[] { /* none */ });
                var resolver = new LoadShipperResolver();

                VendorDispatcher.ConfigureForTesting(
                    enabled: true, fireAndForget: false,
                    registry: emptyRegistry,
                    profileRepository: profileRepo,
                    auditRepository: auditRepo,
                    shipperResolver: resolver);

                var loadId = DbHelper.NewTestLoadId("d6");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                TestHarness.AssertEqual(0, TestFourKitesAdapter.DispatchCallCount,
                    "no adapter to dispatch to");

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("SKIPPED", status);

                var errMsg = await DbHelper.ReadOutboundAsync<string>(txId, "ErrorMessage");
                TestHarness.AssertContains(errMsg, "No adapter registered",
                    "ErrorMessage should explain why we skipped");
            }).GetAwaiter().GetResult();
        }
    }
}
