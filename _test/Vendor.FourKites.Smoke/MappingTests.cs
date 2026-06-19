using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vendor.Common.Events;
using Vendor.FourKites.Mapping;

namespace Vendor.FourKites.Smoke
{
    /// <summary>
    /// Smoke tests for PayloadBuilder after the FK spec rewrite.
    /// LoadStatusMapper tests stay because that mapper is still useful for any vendor that
    /// wants EDI 214 codes — it's just not invoked by the FK adapter anymore (FK does its own
    /// tracking once driverPhone is handed off, so LoadStatusEvent/LocationReportedEvent skip).
    /// </summary>
    internal static class MappingTests
    {
        public static void RegisterAll()
        {
            // ─── LoadStatusMapper (kept for cross-vendor utility) ──────────

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

            // ─── PayloadBuilder.BuildLoadCreate ────────────────────────────

            TestHarness.Run("Payload. BuildLoadCreate wraps in 'load' envelope with FK-spec fields", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);
                var body = JObject.Parse(result.Json);

                TestHarness.AssertNotNull(body["load"], "top-level 'load' envelope");
                TestHarness.AssertNotNull(body["additionalData"], "additionalData");

                var load = body["load"];
                TestHarness.AssertEqual("LOAD123", load["loadNumber"]?.ToString());
                TestHarness.AssertEqual("VCTR", load["carrier"]?.ToString(),
                    "carrier is a string (SCAC), not an object");
                TestHarness.Assert(load["haulType"] is JArray, "haulType is an array");
                TestHarness.AssertEqual("brokered_load", load["haulType"]?[0]?.ToString());
            });

            TestHarness.Run("Payload. BuildLoadCreate puts driver+equipment under trackingInfo", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);
                var load = JObject.Parse(result.Json)["load"];

                TestHarness.AssertNotNull(load["trackingInfo"], "trackingInfo present");
                TestHarness.AssertEqual("555-0100", load["trackingInfo"]?["driverPhone"]?.ToString());
                TestHarness.AssertEqual("TRUCK-001", load["trackingInfo"]?["truckNumber"]?.ToString());
                TestHarness.AssertEqual("TRL-001", load["trackingInfo"]?["trailerNumber"]?.ToString());

                // FK spec: no top-level driver or equipment objects
                TestHarness.AssertNull(load["driver"], "no top-level driver");
                TestHarness.AssertNull(load["equipment"], "no top-level equipment");
            });

            TestHarness.Run("Payload. BuildLoadCreate stops use stopType not role", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);
                var stops = JObject.Parse(result.Json)["load"]?["stops"] as JArray;

                TestHarness.AssertNotNull(stops, "stops array present");
                TestHarness.AssertEqual(2, stops.Count);
                TestHarness.AssertEqual("pickup", stops[0]["stopType"]?.ToString());
                TestHarness.AssertEqual("delivery", stops[1]["stopType"]?.ToString());
                TestHarness.AssertNull(stops[0]["role"], "no role enum");
            });

            TestHarness.Run("Payload. BuildLoadCreate stop datetime has no Z suffix", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();
                evt.Stops[0].ScheduledArrivalUtc = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
                evt.Stops[0].ScheduledDepartureUtc = new DateTime(2026, 1, 15, 16, 0, 0, DateTimeKind.Utc);

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);

                // FK expects ISO 8601 in stop's local timezone, NO Z, NO offset.
                // Check the raw JSON string directly — if we parse with JObject, Newtonsoft
                // auto-converts ISO date strings to DateTime tokens, which then render back
                // in current-culture format on ToString(). The raw JSON is what FK sees.
                TestHarness.AssertContains(result.Json, "\"earliestAppointmentTime\":\"2026-01-15T08:00:00\"",
                    "earliestAppointmentTime ISO format, no Z");
                TestHarness.AssertContains(result.Json, "\"latestAppointmentTime\":\"2026-01-15T16:00:00\"",
                    "latestAppointmentTime ISO format, no Z");
                TestHarness.Assert(!result.Json.Contains("08:00:00Z"), "no Z suffix");
                TestHarness.Assert(!result.Json.Contains("08:00:00+") && !result.Json.Contains("08:00:00-"),
                    "no offset");
            });

            TestHarness.Run("Payload. BuildLoadCreate stop lat/lon emitted as decimals (not strings)", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();
                evt.Stops[0].Latitude = "35.1495";
                evt.Stops[0].Longitude = "-90.0490";

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);

                var stop = JObject.Parse(result.Json)["load"]?["stops"]?[0];
                TestHarness.AssertEqual(JTokenType.Float, stop["latitude"]?.Type);
                TestHarness.AssertEqual(JTokenType.Float, stop["longitude"]?.Type);
            });

            TestHarness.Run("Payload. BuildLoadCreate omits billToCode and shipper at top level", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadCreate(evt, cfg);
                var body = JObject.Parse(result.Json);

                TestHarness.AssertNull(body["billToCode"], "billToCode not in body (per FK spec)");
                TestHarness.AssertNull(body["shipper"], "shipper derived from API key, not in body");
                TestHarness.AssertNull(body["load"]?["billToCode"], "billToCode also not inside load");
                TestHarness.AssertNull(body["load"]?["shipper"], "shipper not inside load");
            });

            // ─── PayloadBuilder.BuildLoadUpdate ────────────────────────────

            TestHarness.Run("Payload. BuildLoadUpdate sets simpleUpdate=false (partial update)", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadUpdate(evt, cfg);
                var body = JObject.Parse(result.Json);

                TestHarness.AssertEqual(false, body["simpleUpdate"]?.Value<bool>(),
                    "simpleUpdate=false means partial; true would mean full snapshot");
            });

            TestHarness.Run("Payload. BuildLoadUpdate copies trackingInfo to top level for partial update", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var result = PayloadBuilder.BuildLoadUpdate(evt, cfg);
                var body = JObject.Parse(result.Json);

                TestHarness.AssertNotNull(body["trackingInfo"], "top-level trackingInfo for partial Update");
                TestHarness.AssertEqual("555-0100", body["trackingInfo"]?["driverPhone"]?.ToString());
            });

            // ─── PayloadBuilder.BuildLoadDelete ────────────────────────────

            TestHarness.Run("Payload. BuildLoadDelete carries only trackingIds (no reason field)", () =>
            {
                var result = PayloadBuilder.BuildLoadDelete(987654321L);
                var body = JObject.Parse(result.Json);

                TestHarness.Assert(body["trackingIds"] is JArray, "trackingIds is an array");
                var ids = (JArray)body["trackingIds"];
                TestHarness.AssertEqual(1, ids.Count);
                TestHarness.AssertEqual(987654321L, ids[0].Value<long>());

                // FK delete payload has no reason / status / cancelled-vs-delivered field
                TestHarness.AssertNull(body["reason"], "no reason field in FK delete");
                TestHarness.AssertNull(body["status"], "no status field in FK delete");
            });

            // ─── PayloadBuilder.BuildDocumentUpload ────────────────────────

            TestHarness.Run("Payload. BuildDocumentUpload uses base64 JSON shape (not multipart)", () =>
            {
                var cfg = NewCfg();
                var evt = new DocumentAvailableEvent
                {
                    VectorLoadId = "LOAD123",
                    SourceSystem = "Test",
                    DocumentType = DocumentType.ProofOfDelivery,
                    FileName = "pod.pdf",
                    MimeType = "application/pdf"
                };

                var result = PayloadBuilder.BuildDocumentUpload(evt, cfg);
                var body = JObject.Parse(result.Json);

                TestHarness.AssertEqual("loadNumber", body["load"]?["identifier"]?.ToString());
                TestHarness.AssertEqual("LOAD123", body["load"]?["value"]?.ToString());

                var docs = body["documents"] as JArray;
                TestHarness.AssertNotNull(docs, "documents array present");
                TestHarness.AssertEqual(1, docs.Count);
                TestHarness.AssertEqual("pdf", docs[0]["type"]?.ToString());
                TestHarness.AssertEqual("DR", docs[0]["document_type"]?.ToString(),
                    "ProofOfDelivery -> DR (Delivery Receipt)");
                TestHarness.AssertNotNull(docs[0]["base64_content"],
                    "base64_content key present (empty for Phase 1)");
            });

            TestHarness.Run("Payload. BuildDocumentUpload maps BillOfLading to BL", () =>
            {
                var cfg = NewCfg();
                var evt = new DocumentAvailableEvent
                {
                    VectorLoadId = "L",
                    SourceSystem = "T",
                    DocumentType = DocumentType.BillOfLading,
                    FileName = "bol.pdf",
                    MimeType = "application/pdf"
                };
                var result = PayloadBuilder.BuildDocumentUpload(evt, cfg);
                var docType = JObject.Parse(result.Json)["documents"]?[0]?["document_type"]?.ToString();
                TestHarness.AssertEqual("BL", docType);
            });

            // ─── RequestId generation ──────────────────────────────────────

            TestHarness.Run("Payload. Every builder generates a fresh RequestId", () =>
            {
                var cfg = NewCfg();
                var evt = NewLoadAssignedEvent();

                var ids = new List<string>
                {
                    PayloadBuilder.BuildLoadCreate(evt, cfg).RequestId,
                    PayloadBuilder.BuildLoadUpdate(evt, cfg).RequestId,
                    PayloadBuilder.BuildLoadDelete(1).RequestId,
                    PayloadBuilder.BuildDocumentUpload(new DocumentAvailableEvent
                    {
                        VectorLoadId = "L", SourceSystem = "T",
                        DocumentType = DocumentType.BillOfLading,
                        FileName = "f", MimeType = "x"
                    }, cfg).RequestId
                };

                foreach (var id in ids)
                    TestHarness.AssertNotNull(id, "RequestId generated");

                TestHarness.AssertEqual(ids.Count, ids.Distinct().Count(),
                    "RequestIds are unique per build");
            });
        }

        // ─── Test fixtures ──────────────────────────────────────────────────

        private static FourKitesConfig NewCfg() => new FourKitesConfig
        {
            ApiKey          = "test-key",
            BillToCode      = "2215324",
            VectorScac      = "VCTR",
            Environment     = "staging",
            DefaultHaulType = "brokered_load",
            RateLimit       = new RateLimitConfig { RequestsPerSecond = 1, BurstSize = 5 }
        };

        private static LoadAssignedEvent NewLoadAssignedEvent() => new LoadAssignedEvent
        {
            VectorLoadId   = "LOAD123",
            SourceSystem   = "Test",
            ExternalLoadId = "EXT-123",
            Carrier        = new CarrierInfo { Scac = "ABCD", Name = "Test Carrier" },
            Driver         = new DriverInfo  { Name = "Driver A", Phone = "555-0100" },
            Equipment      = new EquipmentInfo
            {
                TruckNumber = "TRUCK-001", TrailerNumber = "TRL-001", TrailerType = "DRY_VAN"
            },
            Stops = new List<StopInfo>
            {
                new StopInfo
                {
                    SequenceNumber = 1, Role = StopRole.Pickup,
                    Name = "Memphis Warehouse",
                    AddressLine1 = "123 Main St", City = "Memphis", State = "TN", PostalCode = "38103"
                },
                new StopInfo
                {
                    SequenceNumber = 2, Role = StopRole.Delivery,
                    Name = "Nashville DC",
                    AddressLine1 = "456 Broadway", City = "Nashville", State = "TN", PostalCode = "37203"
                }
            }
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
