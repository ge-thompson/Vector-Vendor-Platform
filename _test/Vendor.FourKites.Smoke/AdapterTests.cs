using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.FourKites.Smoke
{
    /// <summary>
    /// Smoke tests for FourKitesAdapter after the FK spec rewrite.
    ///
    /// New routing rules (per docs.fourkites.com/api-reference):
    ///   LoadAssignedEvent         -> POST /api/v1/tracking (Create) when no cross-ref
    ///                                PATCH /api/v1/tracking/{loadId} (Update) when cross-ref exists
    ///   LoadTrackingStoppedEvent  -> POST /api/v1/tracking/delete_loads (or Skipped if no cross-ref)
    ///   DocumentAvailableEvent    -> POST /document-data/upload
    ///   LoadStatusEvent           -> SKIPPED (FK does its own tracking)
    ///   LocationReportedEvent     -> SKIPPED (FK does its own tracking)
    ///   LoadCreatedEvent          -> SKIPPED (Phase 2 / FBS origin path)
    ///
    /// Tests pass null for LoadCrossReferenceStore so the adapter degrades to Create-only
    /// behavior. The Update and Delete-with-cross-ref paths are exercised in the integration
    /// test layer (with a real VendorAPI_FK database).
    /// </summary>
    internal static class AdapterTests
    {
        public static void RegisterAll()
        {
            // ─── Routing: LoadAssignedEvent -> Create (no cross-ref) ──────

            TestHarness.RunAsync("Adapter. LoadAssignedEvent without cross-ref hits POST /api/v1/tracking", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK,
                    @"{""loadId"":420645495,""message"":""Load created successfully"",""statusCode"":200}");

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var result = await adapter.DispatchAsync(NewLoadAssignedEvent(), NewProfile());

                    TestHarness.Assert(result.Success, "Create succeeded");
                    TestHarness.AssertEqual(200, result.HttpStatusCode.Value);
                    TestHarness.AssertEqual("420645495", result.VendorLoadId,
                        "FK loadId captured from response");
                    TestHarness.AssertEqual("LOAD_CREATION", result.ExpectedCallbackType);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count);
                TestHarness.AssertEqual("POST", mock.Requests[0].Method.Method);
                TestHarness.AssertContains(mock.Requests[0].RequestUri.ToString(),
                    "/api/v1/tracking", "Create path");
                TestHarness.Assert(
                    !mock.Requests[0].RequestUri.ToString().Contains("/delete_loads"),
                    "not the delete URL");
            }).GetAwaiter().GetResult();

            // ─── Auth header: lowercase 'apikey' ──────────────────────────

            TestHarness.RunAsync("Adapter. Uses lowercase 'apikey' auth header", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK, @"{""loadId"":1}");

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    await adapter.DispatchAsync(NewLoadAssignedEvent(), NewProfile());
                }

                var req = mock.Requests[0];
                System.Collections.Generic.IEnumerable<string> values;
                var hasApikey = req.Headers.TryGetValues("apikey", out values);
                TestHarness.Assert(hasApikey, "request has 'apikey' header (lowercase)");
                TestHarness.AssertEqual("test-api-key", values.First(),
                    "raw key (no Bearer prefix)");

                // FK spec is explicit: no X-Api-Key, no Authorization header
                TestHarness.Assert(!req.Headers.TryGetValues("X-Api-Key", out _),
                    "no X-Api-Key");
                TestHarness.Assert(!req.Headers.Contains("X-FK-API-Key"),
                    "no X-FK-API-Key (the old pre-rewrite name)");
            }).GetAwaiter().GetResult();

            // ─── Routing: LoadTrackingStoppedEvent without cross-ref -> Skipped ─

            TestHarness.RunAsync("Adapter. LoadTrackingStopped without cross-ref returns Skipped", async () =>
            {
                var mock = new MockHttpMessageHandler();

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var result = await adapter.DispatchAsync(new LoadTrackingStoppedEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T", Reason = "CANCELLED"
                    }, NewProfile());

                    TestHarness.Assert(!result.Success, "Skipped is not Success");
                    TestHarness.AssertContains(result.ErrorMessage, "No FK loadId",
                        "tells operator why we skipped");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count,
                    "no HTTP call without cross-ref");
            }).GetAwaiter().GetResult();

            // ─── Routing: DocumentAvailableEvent -> /document-data/upload ─

            TestHarness.RunAsync("Adapter. DocumentAvailableEvent hits /document-data/upload", async () =>
            {
                var mock = new MockHttpMessageHandler();
                mock.QueueResponse(HttpStatusCode.OK, @"{""message"":""ok""}");

                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var evt = new DocumentAvailableEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T",
                        DocumentType = DocumentType.ProofOfDelivery,
                        FileName = "pod.pdf", MimeType = "application/pdf"
                    };
                    await adapter.DispatchAsync(evt, NewProfile());
                }

                TestHarness.AssertEqual(1, mock.Requests.Count);
                TestHarness.AssertContains(mock.Requests[0].RequestUri.ToString(),
                    "/document-data/upload", "document upload path");
                TestHarness.AssertContains(mock.SentBodies[0], "base64_content",
                    "base64 JSON shape, not multipart");
            }).GetAwaiter().GetResult();

            // ─── Skipped event types ──────────────────────────────────────

            TestHarness.RunAsync("Adapter. LoadStatusEvent returns Skipped (FK does own tracking)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var result = await adapter.DispatchAsync(new LoadStatusEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T",
                        StatusType = LoadStatusType.ArrivedAtPickup,
                        StatusTimeUtc = DateTime.UtcNow
                    }, NewProfile());

                    TestHarness.Assert(!result.Success, "Skipped is not Success");
                    TestHarness.AssertContains(result.ErrorMessage, "FK does its own truck tracking",
                        "skip reason is explicit");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count, "no HTTP call");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. LocationReportedEvent returns Skipped", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var result = await adapter.DispatchAsync(new LocationReportedEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T",
                        Latitude = "35.1", Longitude = "-90.1",
                        LocatedAtUtc = DateTime.UtcNow
                    }, NewProfile());

                    TestHarness.Assert(!result.Success, "Skipped is not Success");
                    TestHarness.AssertContains(result.ErrorMessage, "FK does its own truck tracking", "skip reason");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. LoadCreatedEvent returns Skipped (Phase 2 deferred)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var result = await adapter.DispatchAsync(new LoadCreatedEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T", Mode = "TL"
                    }, NewProfile());

                    TestHarness.Assert(!result.Success, "LoadCreatedEvent skipped in Phase 1");
                    TestHarness.AssertContains(result.ErrorMessage, "LoadAssignedEvent", "skip reason");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count);
            }).GetAwaiter().GetResult();

            // ─── CanHandle ────────────────────────────────────────────────

            TestHarness.RunAsync("Adapter. CanHandle returns false for GenericLoadEvent", async () =>
            {
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(new MockHttpMessageHandler(), TimeSpan.FromSeconds(5)),
                    crossRef: null))
                {
                    TestHarness.Assert(!adapter.CanHandle(new GenericLoadEvent
                        { VectorLoadId = "L", SourceSystem = "T", EventName = "weird" }),
                        "decline GenericLoadEvent");

                    TestHarness.Assert(adapter.CanHandle(new LoadAssignedEvent
                        { VectorLoadId = "L", SourceSystem = "T" }),
                        "accept LoadAssignedEvent");
                }
                await Task.CompletedTask;
            }).GetAwaiter().GetResult();

            // ─── Failure modes ────────────────────────────────────────────

            TestHarness.RunAsync("Adapter. Malformed ConfigJson returns Permanent failure (does not throw)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var badProfile = new ClientProfile
                    {
                        VendorName = "FourKites",
                        ConfigJson = "{this is not json"
                    };

                    var result = await adapter.DispatchAsync(NewLoadAssignedEvent(), badProfile);

                    TestHarness.Assert(!result.Success, "should be failure");
                    TestHarness.AssertEqual("Permanent", result.ErrorCategory);
                    TestHarness.AssertContains(result.ErrorMessage, "ConfigJson",
                        "error explains config problem");
                }

                TestHarness.AssertEqual(0, mock.Requests.Count,
                    "no HTTP call when config is bad");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Rate-limit exhaustion returns RateLimited (no HTTP call)", async () =>
            {
                var mock = new MockHttpMessageHandler();
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(mock, TimeSpan.FromSeconds(5)), crossRef: null))
                {
                    var profile = NewProfile(burstSize: 1, requestsPerSecond: 1);

                    mock.QueueResponse(HttpStatusCode.OK, @"{""loadId"":1}");
                    var r1 = await adapter.DispatchAsync(NewLoadAssignedEvent(), profile);
                    TestHarness.Assert(r1.Success, "first dispatch succeeds");

                    var r2 = await adapter.DispatchAsync(NewLoadAssignedEvent(), profile);
                    TestHarness.Assert(!r2.Success, "second is rate-limited");
                    TestHarness.AssertEqual("RateLimit", r2.ErrorCategory);
                }

                TestHarness.AssertEqual(1, mock.Requests.Count,
                    "rate-limit blocks 2nd call before HTTP");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Adapter. Null event returns failure (does not throw)", async () =>
            {
                using (var adapter = new FourKitesAdapter(
                    new FourKitesClient(new MockHttpMessageHandler(), TimeSpan.FromSeconds(5)),
                    crossRef: null))
                {
                    var result = await adapter.DispatchAsync(null, NewProfile());
                    TestHarness.Assert(!result.Success, "null event should produce a failure");
                }
            }).GetAwaiter().GetResult();
        }

        // ─── Fixtures ──────────────────────────────────────────────────────

        private static LoadAssignedEvent NewLoadAssignedEvent() => new LoadAssignedEvent
        {
            VectorLoadId   = "999001",
            SourceSystem   = "Test",
            Carrier        = new CarrierInfo { Scac = "ABCD", Name = "Test Carrier" },
            Driver         = new DriverInfo  { Name = "Driver A", Phone = "+15550100" },
            Equipment      = new EquipmentInfo { TruckNumber = "TRUCK-001", TrailerNumber = "TRL-001" },
            Stops = new System.Collections.Generic.List<StopInfo>
            {
                new StopInfo
                {
                    SequenceNumber = 1, Role = StopRole.Pickup,
                    Name = "Memphis Warehouse", AddressLine1 = "123 Main",
                    City = "Memphis", State = "TN",
                    ScheduledArrivalUtc = new DateTime(2026,1,15,8,0,0,DateTimeKind.Utc),
                    ScheduledDepartureUtc = new DateTime(2026,1,15,16,0,0,DateTimeKind.Utc)
                },
                new StopInfo
                {
                    SequenceNumber = 2, Role = StopRole.Delivery,
                    Name = "Nashville DC", AddressLine1 = "456 Broadway",
                    City = "Nashville", State = "TN",
                    ScheduledArrivalUtc = new DateTime(2026,1,16,8,0,0,DateTimeKind.Utc),
                    ScheduledDepartureUtc = new DateTime(2026,1,16,16,0,0,DateTimeKind.Utc)
                }
            }
        };

        private static ClientProfile NewProfile(int burstSize = 100, int requestsPerSecond = 50)
        {
            return new ClientProfile
            {
                ProfileId = 1,
                ShipperCode = "VECTOR_DEFAULT",
                VendorName = "FourKites",
                IsActive = true,
                EnabledEvents = "LoadAssignedEvent,LoadTrackingStoppedEvent,DocumentAvailableEvent",
                ConfigJson = $@"{{
                    ""apiKey"": ""test-api-key"",
                    ""billToCode"": ""2215324"",
                    ""vectorScac"": ""VCTR"",
                    ""environment"": ""staging"",
                    ""defaultHaulType"": ""brokered_load"",
                    ""rateLimit"": {{ ""burstSize"": {burstSize}, ""requestsPerSecond"": {requestsPerSecond} }}
                }}"
            };
        }
    }
}
