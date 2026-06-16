using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vendor.Common.Events;
using Vendor.FourKites.Mapping;

namespace Vendor.FourKites.Smoke
{
    internal static class MappingTests
    {
        public static void RegisterAll()
        {
            // ─── LoadStatusMapper ──────────────────────────────────────────

            TestHarness.Run("Mapper. Canonical codes returned for known statuses", () =>
            {
                TestHarness.AssertEqual("X1", LoadStatusMapper.MapStatusType(LoadStatusType.ArrivedAtPickup));
                TestHarness.AssertEqual("AF", LoadStatusMapper.MapStatusType(LoadStatusType.DepartedPickup));
                TestHarness.AssertEqual("X3", LoadStatusMapper.MapStatusType(LoadStatusType.ArrivedAtDelivery));
                TestHarness.AssertEqual("CD", LoadStatusMapper.MapStatusType(LoadStatusType.DepartedDelivery));
                TestHarness.AssertEqual("D1", LoadStatusMapper.MapStatusType(LoadStatusType.Delivered));
                TestHarness.AssertEqual("OA", LoadStatusMapper.MapStatusType(LoadStatusType.Dispatched));
            });

            TestHarness.Run("Mapper. Other returns X9 catch-all", () =>
            {
                TestHarness.AssertEqual("X9", LoadStatusMapper.MapStatusType(LoadStatusType.Other));
            });

            TestHarness.Run("Mapper. MapFromEvent prefers canonical when StatusType matches", () =>
            {
                var evt = new LoadStatusEvent
                {
                    StatusType = LoadStatusType.ArrivedAtPickup,
                    SourceStatusCode = "ARRIVED_PICKUP",
                    StatusTimeUtc = DateTime.UtcNow
                };
                TestHarness.AssertEqual("X1", LoadStatusMapper.MapFromEvent(evt));
            });

            TestHarness.Run("Mapper. MapFromEvent falls back to SourceStatusCode for Other", () =>
            {
                var evt = new LoadStatusEvent
                {
                    StatusType = LoadStatusType.Other,
                    SourceStatusCode = "CUSTOM_CODE",
                    StatusTimeUtc = DateTime.UtcNow
                };
                TestHarness.AssertEqual("CUSTOM_CODE", LoadStatusMapper.MapFromEvent(evt),
                    "should pass through SourceStatusCode for Other");
            });

            // ─── PayloadBuilder ────────────────────────────────────────────

            TestHarness.Run("Payload. BuildLoadCreated includes core fields + stops", () =>
            {
                var cfg = NewCfg();
                var evt = new LoadCreatedEvent
                {
                    VectorLoadId = "LOAD123",
                    SourceSystem = "Test",
                    Mode = "TL",
                    EquipmentType = "Dry Van",
                    Weight = 30000m,
                    WeightUnit = "LB",
                    Origin = new StopInfo
                    {
                        SequenceNumber = 1, Role = StopRole.Pickup,
                        City = "Memphis", State = "TN", PostalCode = "38103"
                    },
                    Destination = new StopInfo
                    {
                        SequenceNumber = 2, Role = StopRole.Delivery,
                        City = "Dallas", State = "TX"
                    },
                    References = new List<ReferenceNumber>
                    {
                        new ReferenceNumber { Type = "BOL", Value = "BOL-99" }
                    }
                };

                var result = PayloadBuilder.BuildLoadCreated(evt, cfg);
                var jo = JObject.Parse(result.Json);

                TestHarness.AssertEqual("VECTOR", jo["billToCode"]?.ToString());
                TestHarness.AssertEqual("LOAD123", jo["loadNumber"]?.ToString());
                TestHarness.AssertNotNull(jo["requestId"], "requestId");
                TestHarness.AssertNotNull(result.RequestId, "result.RequestId");
                TestHarness.AssertEqual("TL", jo["mode"]?.ToString());
                TestHarness.AssertEqual("Memphis", jo["origin"]?["city"]?.ToString());
                TestHarness.AssertEqual("Dallas", jo["destination"]?["city"]?.ToString());
                TestHarness.AssertEqual(1, jo["references"]?.Count(), "1 reference");
            });

            TestHarness.Run("Payload. BuildLoadAssigned omits null sub-objects", () =>
            {
                var cfg = NewCfg();
                var evt = new LoadAssignedEvent
                {
                    VectorLoadId = "L",
                    SourceSystem = "T",
                    Carrier = new CarrierInfo { Scac = "ABCD", Name = "Acme" }
                    // Driver + Equipment intentionally null
                };
                var result = PayloadBuilder.BuildLoadAssigned(evt, cfg);
                var jo = JObject.Parse(result.Json);

                TestHarness.AssertEqual("ABCD", jo["carrier"]?["scac"]?.ToString());
                TestHarness.AssertNull(jo["driver"], "driver should be absent when not set");
                TestHarness.AssertNull(jo["equipment"], "equipment should be absent when not set");
            });

            TestHarness.Run("Payload. BuildLocationReported includes lat/lon as strings", () =>
            {
                var cfg = NewCfg();
                var evt = new LocationReportedEvent
                {
                    VectorLoadId = "L",
                    SourceSystem = "T",
                    Latitude = "35.149534",
                    Longitude = "-90.048980",
                    LocatedAtUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
                    SpeedMph = 65.5,
                    Heading = 180
                };
                var result = PayloadBuilder.BuildLocationReported(evt, cfg);

                // Use JsonTextReader with DateParseHandling=None so Newtonsoft doesn't
                // auto-convert ISO date strings into DateTime tokens (which would then
                // format back in current culture instead of preserving the original string).
                JObject jo;
                using (var sr = new System.IO.StringReader(result.Json))
                using (var jr = new Newtonsoft.Json.JsonTextReader(sr) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                {
                    jo = JObject.Load(jr);
                }

                TestHarness.AssertEqual("35.149534", jo["latitude"]?.ToString());
                TestHarness.AssertEqual("-90.048980", jo["longitude"]?.ToString());
                TestHarness.AssertEqual(65.5, jo["speedMph"]?.Value<double>() ?? 0);
                TestHarness.AssertContains(jo["locatedAt"]?.Value<string>(), "2026-06-01", "ISO date");
            });

            TestHarness.Run("Payload. BuildLoadStatus maps StatusType to EDI 214 code", () =>
            {
                var cfg = NewCfg();
                var evt = new LoadStatusEvent
                {
                    VectorLoadId = "L",
                    SourceSystem = "T",
                    StatusType = LoadStatusType.ArrivedAtPickup,
                    StatusTimeUtc = DateTime.UtcNow,
                    SourceStatusCode = "RAW_CODE"
                };
                var result = PayloadBuilder.BuildLoadStatus(evt, cfg);
                var jo = JObject.Parse(result.Json);

                TestHarness.AssertEqual("X1", jo["statusCode"]?.ToString(),
                    "ArrivedAtPickup → X1");
                TestHarness.AssertEqual("RAW_CODE", jo["sourceCode"]?.ToString(),
                    "sourceCode preserved");
            });

            TestHarness.Run("Payload. BuildTrackingStopped includes reason", () =>
            {
                var cfg = NewCfg();
                var evt = new LoadTrackingStoppedEvent
                {
                    VectorLoadId = "L", SourceSystem = "T", Reason = "CANCELLED"
                };
                var result = PayloadBuilder.BuildTrackingStopped(evt, cfg);
                var jo = JObject.Parse(result.Json);
                TestHarness.AssertEqual("CANCELLED", jo["reason"]?.ToString());
            });

            TestHarness.Run("Payload. BuildDocumentMetadata includes documentType", () =>
            {
                var cfg = NewCfg();
                var evt = new DocumentAvailableEvent
                {
                    VectorLoadId = "L", SourceSystem = "T",
                    DocumentType = DocumentType.ProofOfDelivery,
                    FileName = "pod.pdf", MimeType = "application/pdf"
                };
                var result = PayloadBuilder.BuildDocumentMetadata(evt, cfg);
                var jo = JObject.Parse(result.Json);
                TestHarness.AssertEqual("ProofOfDelivery", jo["documentType"]?.ToString());
                TestHarness.AssertEqual("pod.pdf", jo["fileName"]?.ToString());
            });

            TestHarness.Run("Payload. Every event-type payload includes billToCode + loadNumber + requestId", () =>
            {
                var cfg = NewCfg();
                var payloads = new[]
                {
                    PayloadBuilder.BuildLoadCreated(new LoadCreatedEvent { VectorLoadId = "L", SourceSystem = "T" }, cfg).Json,
                    PayloadBuilder.BuildLoadAssigned(new LoadAssignedEvent { VectorLoadId = "L", SourceSystem = "T" }, cfg).Json,
                    PayloadBuilder.BuildLocationReported(new LocationReportedEvent { VectorLoadId = "L", SourceSystem = "T", Latitude = "0", Longitude = "0", LocatedAtUtc = DateTime.UtcNow }, cfg).Json,
                    PayloadBuilder.BuildLoadStatus(new LoadStatusEvent { VectorLoadId = "L", SourceSystem = "T", StatusType = LoadStatusType.Dispatched, StatusTimeUtc = DateTime.UtcNow }, cfg).Json,
                    PayloadBuilder.BuildTrackingStopped(new LoadTrackingStoppedEvent { VectorLoadId = "L", SourceSystem = "T" }, cfg).Json,
                    PayloadBuilder.BuildDocumentMetadata(new DocumentAvailableEvent { VectorLoadId = "L", SourceSystem = "T", DocumentType = DocumentType.BillOfLading, FileName = "f", MimeType = "x" }, cfg).Json
                };

                foreach (var p in payloads)
                {
                    var jo = JObject.Parse(p);
                    TestHarness.AssertEqual("VECTOR", jo["billToCode"]?.ToString(), $"billToCode in {p.Substring(0, Math.Min(50, p.Length))}");
                    TestHarness.AssertEqual("L", jo["loadNumber"]?.ToString(), "loadNumber");
                    TestHarness.AssertNotNull(jo["requestId"], "requestId");
                }
            });
        }

        private static FourKitesConfig NewCfg() => new FourKitesConfig
        {
            ApiKey = "test-key",
            BillToCode = "VECTOR",
            BaseUrl = "https://api.fourkites.com",
            RateLimit = new RateLimitConfig { RequestsPerSecond = 10, BurstSize = 20 }
        };
    }

    internal static class JArrayExtensions
    {
        public static int Count(this JToken token)
        {
            if (token is JArray arr) return arr.Count;
            return 0;
        }
    }
}
