using System;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Events;
using Vendor.Common.Persistence;

namespace Vendor.Common.Dispatch.Smoke
{
    internal static class DispatchHappyPathTests
    {
        /// <summary>
        /// Each test:
        ///   1. Resets dispatcher + adapter state
        ///   2. Configures dispatcher with test collaborators
        ///   3. Sets adapter behavior
        ///   4. Dispatches an event with a unique LoadId
        ///   5. Queries DB to verify the audit row matches expectations
        /// </summary>
        public static void RegisterAll()
        {
            TestHarness.RunAsync("C1. Dispatch with matching profile writes ACK row and calls adapter once", async () =>
            {
                SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("c1");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId,
                    SourceSystem = "Dispatch.Smoke",
                    Latitude = "35.0", Longitude = "-90.0",
                    LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                TestHarness.AssertEqual(1, TestFourKitesAdapter.DispatchCallCount,
                    "adapter.DispatchAsync should have been called once");

                var rowCount = await DbHelper.CountOutboundRowsAsync(loadId);
                TestHarness.AssertEqual(1, rowCount, "one outbound row written");

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                TestHarness.Assert(txId > 0, "TransactionId populated");

                var status = await DbHelper.ReadOutboundAsync<string>(txId, "Status");
                TestHarness.AssertEqual("ACK", status);

                var http = await DbHelper.ReadOutboundAsync<int>(txId, "HttpStatusCode");
                TestHarness.AssertEqual(202, http);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("C2. Adapter result fields land in the audit row", async () =>
            {
                SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("c2");
                var evt = new LoadAssignedEvent
                {
                    VectorLoadId = loadId,
                    SourceSystem = "Dispatch.Smoke",
                    Carrier = new CarrierInfo { Scac = "ABCD" }
                };

                VendorDispatcher.Instance.Dispatch(evt);

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var vendorReqId = await DbHelper.ReadOutboundAsync<string>(txId, "VendorRequestId");
                TestHarness.AssertContains(vendorReqId, "test-req-", "VendorRequestId from adapter");

                var reqPayload = await DbHelper.ReadOutboundAsync<string>(txId, "RequestPayload");
                TestHarness.AssertContains(reqPayload, "test", "RequestPayload from adapter");

                var responseBody = await DbHelper.ReadOutboundAsync<string>(txId, "ResponseBody");
                TestHarness.AssertContains(responseBody, "accepted", "ResponseBody from adapter");

                var duration = await DbHelper.ReadOutboundAsync<int>(txId, "DurationMs");
                TestHarness.Assert(duration > 0, $"DurationMs should be positive, got {duration}");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("C3. DispatchAsync (awaitable variant) works", async () =>
            {
                SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("c3");
                var evt = new LoadCreatedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke", Mode = "TL"
                };

                await VendorDispatcher.Instance.DispatchAsync(evt).ConfigureAwait(false);

                TestHarness.AssertEqual(1, TestFourKitesAdapter.DispatchCallCount,
                    "adapter called once via DispatchAsync");

                var status = await DbHelper.ReadOutboundAsync<string>(
                    await DbHelper.GetSingleTransactionIdAsync(loadId), "Status");
                TestHarness.AssertEqual("ACK", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("C4. Disabled dispatcher writes no rows and does not call adapter", async () =>
            {
                SetupDispatcher(enabled: false);
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("c4");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                TestHarness.AssertEqual(0, TestFourKitesAdapter.DispatchCallCount,
                    "adapter should NOT be called when Enabled=false");

                var rowCount = await DbHelper.CountOutboundRowsAsync(loadId);
                TestHarness.AssertEqual(0, rowCount, "no rows written when disabled");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("C5. Dispatcher populates Duration even when adapter didn't", async () =>
            {
                SetupDispatcher();
                // Use a behavior that returns a result without setting Duration explicitly
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnFailRateLimited;

                var loadId = DbHelper.NewTestLoadId("c5");
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var duration = await DbHelper.ReadOutboundAsync<int?>(txId, "DurationMs");
                TestHarness.AssertNotNull(duration, "DurationMs should be populated by dispatcher's Stopwatch");
                TestHarness.Assert(duration.Value >= 0, "DurationMs non-negative");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("C6. VectorLoadId and EventTypeName audit fields correct", async () =>
            {
                SetupDispatcher();
                TestFourKitesAdapter.Behavior = TestFourKitesAdapter.BehaviorMode.ReturnSuccess;

                var loadId = DbHelper.NewTestLoadId("c6");
                var evt = new LoadStatusEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Dispatch.Smoke",
                    StatusType = LoadStatusType.ArrivedAtPickup,
                    StatusTimeUtc = DateTime.UtcNow
                };

                VendorDispatcher.Instance.Dispatch(evt);

                var txId = await DbHelper.GetSingleTransactionIdAsync(loadId);
                var loadIdInDb = await DbHelper.ReadOutboundAsync<string>(txId, "VectorLoadId");
                TestHarness.AssertEqual(loadId, loadIdInDb, "VectorLoadId in DB matches event");

                var eventType = await DbHelper.ReadOutboundAsync<string>(txId, "EventTypeName");
                TestHarness.AssertEqual("LoadStatusEvent", eventType, "EventTypeName matches");

                var source = await DbHelper.ReadOutboundAsync<string>(txId, "SourceSystem");
                TestHarness.AssertEqual("Dispatch.Smoke", source, "SourceSystem matches");
            }).GetAwaiter().GetResult();
        }

        /// <summary>Helper — wires up the dispatcher singleton with the test adapter.</summary>
        internal static void SetupDispatcher(bool enabled = true)
        {
            VendorDispatcher.ResetForTesting();
            TestFourKitesAdapter.Reset();

            var profileRepo = new ClientProfileRepository(DbHelper.ConnectionString);
            var auditRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
            var registry = new VendorAdapterRegistry(new IVendorAdapter[] { new TestFourKitesAdapter() });
            var resolver = new LoadShipperResolver();

            VendorDispatcher.ConfigureForTesting(
                enabled: enabled,
                fireAndForget: false,   // sync so we can assert immediately
                registry: registry,
                profileRepository: profileRepo,
                auditRepository: auditRepo,
                shipperResolver: resolver);
        }
    }
}
