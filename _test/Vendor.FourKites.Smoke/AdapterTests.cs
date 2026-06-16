using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.FourKites.Smoke
{
    internal static class AdapterTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("Adapter. Happy path: ACK with VendorRequestId echoed back", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.Accepted,
                    @"{""requestId"":""server-req-id"",""fourKitesLoadId"":""FK-99""}");

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5))))
                {
                    var evt = new LoadCreatedEvent
                    {
                        VectorLoadId = "LOAD-X", SourceSystem = "Test", Mode = "TL"
                    };
                    var profile = NewProfile();

                    var result = await adapter.DispatchAsync(evt, profile);

                    TestHarness.Assert(result.Success, "result.Success");
                    TestHarness.AssertEqual(202, result.HttpStatusCode.Value);
                    TestHarness.AssertEqual("server-req-id", result.VendorRequestId,
                        "VendorRequestId from FK response wins over locally generated");
                    TestHarness.AssertEqual("FK-99", result.VendorLoadId);
                    TestHarness.AssertContains(result.RequestPayloadJson, "LOAD-X", "payload preserved");
                    TestHarness.AssertContains(result.ResponseBodyJson, "FK-99", "response body preserved");
                    TestHarness.AssertEqual("LOAD_CREATION", result.ExpectedCallbackType,
                        "ExpectedCallbackType set for LoadCreated");
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Different event types route to different endpoints", async () =>
            {
                var mock = new MockHttpMessageHandler();
                // Queue 4 OK responses
                mock.QueueResponse(HttpStatusCode.OK);
                mock.QueueResponse(HttpStatusCode.OK);
                mock.QueueResponse(HttpStatusCode.OK);
                mock.QueueResponse(HttpStatusCode.OK);

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5))))
                {
                    var profile = NewProfile();

                    await adapter.DispatchAsync(new LoadCreatedEvent
                        { VectorLoadId = "L", SourceSystem = "T", Mode = "TL" }, profile);
                    await adapter.DispatchAsync(new LocationReportedEvent
                        { VectorLoadId = "L", SourceSystem = "T", Latitude = "1", Longitude = "2",
                          LocatedAtUtc = DateTime.UtcNow }, profile);
                    await adapter.DispatchAsync(new LoadStatusEvent
                        { VectorLoadId = "L", SourceSystem = "T", StatusType = LoadStatusType.Dispatched,
                          StatusTimeUtc = DateTime.UtcNow }, profile);
                    await adapter.DispatchAsync(new DocumentAvailableEvent
                        { VectorLoadId = "L", SourceSystem = "T",
                          DocumentType = DocumentType.ProofOfDelivery,
                          FileName = "f.pdf", MimeType = "application/pdf" }, profile);
                }

                TestHarness.AssertEqual(4, mock.Requests.Count, "4 dispatches → 4 HTTP calls");

                // Verify each went to a different endpoint
                TestHarness.AssertContains(mock.Requests[0].RequestUri.ToString(), "/v1/loads", "LoadCreated → /v1/loads");
                TestHarness.AssertContains(mock.Requests[1].RequestUri.ToString(), "/v1/loads/location", "Location → /v1/loads/location");
                TestHarness.AssertContains(mock.Requests[2].RequestUri.ToString(), "/v1/loads/status", "Status → /v1/loads/status");
                TestHarness.AssertContains(mock.Requests[3].RequestUri.ToString(), "/v1/loads/documents", "Document → /v1/loads/documents");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. CanHandle returns false for GenericLoadEvent", async () =>
            {
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(new MockHttpMessageHandler(), TimeSpan.FromSeconds(5))))
                {
                    var canHandle = adapter.CanHandle(new GenericLoadEvent
                        { VectorLoadId = "L", SourceSystem = "T", EventName = "weird" });
                    TestHarness.Assert(!canHandle, "FK should decline GenericLoadEvent");

                    var handlesLoadCreated = adapter.CanHandle(new LoadCreatedEvent
                        { VectorLoadId = "L", SourceSystem = "T" });
                    TestHarness.Assert(handlesLoadCreated, "FK should accept LoadCreatedEvent");
                }
                await Task.CompletedTask;
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Malformed ConfigJson returns Permanent failure (does not throw)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5))))
                {
                    var evt = new LoadCreatedEvent
                        { VectorLoadId = "L", SourceSystem = "T", Mode = "TL" };
                    var badProfile = new ClientProfile
                    {
                        VendorName = "FourKites",
                        ConfigJson = "{this is not json"  // malformed
                    };

                    var result = await adapter.DispatchAsync(evt, badProfile);

                    TestHarness.Assert(!result.Success, "should be failure");
                    TestHarness.AssertEqual("Permanent", result.ErrorCategory);
                    TestHarness.AssertContains(result.ErrorMessage, "ConfigJson",
                        "error explains config problem");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count,
                    "no HTTP call should be made when config is bad");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Rate-limit exhaustion returns RateLimited (no HTTP call)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5))))
                {
                    // Configure profile with tiny rate-limit (burst 1)
                    var profile = NewProfile(burstSize: 1, requestsPerSecond: 1);
                    var evt = new LocationReportedEvent
                        { VectorLoadId = "L", SourceSystem = "T",
                          Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow };

                    mock.QueueResponse(HttpStatusCode.OK);
                    var r1 = await adapter.DispatchAsync(evt, profile);
                    TestHarness.Assert(r1.Success, "first dispatch should succeed");

                    // Second immediate dispatch — bucket empty
                    var r2 = await adapter.DispatchAsync(evt, profile);
                    TestHarness.Assert(!r2.Success, "second dispatch should be rate-limited");
                    TestHarness.AssertEqual("RateLimit", r2.ErrorCategory);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count,
                    "rate-limit should block 2nd call before HTTP");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Null event returns failure (does not throw)", async () =>
            {
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(new MockHttpMessageHandler(), TimeSpan.FromSeconds(5))))
                {
                    var result = await adapter.DispatchAsync(null, NewProfile());
                    TestHarness.Assert(!result.Success, "null event should produce a failure result");
                }
            }).GetAwaiter().GetResult();
        }

        private static ClientProfile NewProfile(int burstSize = 100, int requestsPerSecond = 50)
        {
            return new ClientProfile
            {
                ProfileId = 1,
                ShipperCode = "VECTOR_DEFAULT",
                VendorName = "FourKites",
                IsActive = true,
                EnabledEvents = "LoadCreatedEvent,LoadAssignedEvent,LocationReportedEvent,LoadStatusEvent,LoadTrackingStoppedEvent,DocumentAvailableEvent",
                ConfigJson = $@"{{
                    ""apiKey"": ""test-api-key"",
                    ""billToCode"": ""VECTOR"",
                    ""baseUrl"": ""https://api.fourkites.com"",
                    ""rateLimit"": {{ ""burstSize"": {burstSize}, ""requestsPerSecond"": {requestsPerSecond} }}
                }}"
            };
        }
    }
}
