using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.Common.Smoke
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.WriteLine("Vendor.Common Smoke Tests");
            Console.WriteLine(new string('─', 60));
            Console.WriteLine();

            // ─── Section 1: Events ─────────────────────────────────────
            TestHarness.Run("LoadCreatedEvent constructs with all fields", () =>
            {
                var e = new LoadCreatedEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "OTR_API",
                    Mode = "TL",
                    EquipmentType = "Dry Van",
                    Weight = 42000m,
                    WeightUnit = "LB",
                    Origin = new StopInfo
                    {
                        SequenceNumber = 1,
                        Role = StopRole.Pickup,
                        City = "Memphis", State = "TN", PostalCode = "38103",
                        ScheduledArrivalUtc = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc)
                    },
                    Destination = new StopInfo
                    {
                        SequenceNumber = 2,
                        Role = StopRole.Delivery,
                        City = "Dallas", State = "TX", PostalCode = "75201"
                    },
                    References = new List<ReferenceNumber>
                    {
                        new ReferenceNumber { Type = "BOL", Value = "BOL-99" },
                        new ReferenceNumber { Type = "PO",  Value = "PO-1234" }
                    }
                };

                TestHarness.AssertEqual("LOAD12345", e.VectorLoadId);
                TestHarness.AssertEqual(StopRole.Pickup, e.Origin.Role);
                TestHarness.AssertEqual(2, e.References.Count);
                TestHarness.Assert(e.OccurredUtc > DateTime.MinValue, "OccurredUtc auto-set");
            });

            TestHarness.Run("LocationReportedEvent constructs with all fields", () =>
            {
                var e = new LocationReportedEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "OTR_API",
                    Latitude = "35.1495",
                    Longitude = "-90.0490",
                    LocatedAtUtc = DateTime.UtcNow,
                    City = "Memphis", State = "TN",
                    SpeedMph = 62.5,
                    Heading = 180
                };
                TestHarness.AssertEqual("35.1495", e.Latitude);
                TestHarness.AssertEqual(62.5, e.SpeedMph.Value);
            });

            TestHarness.Run("LoadStatusEvent constructs with status type and raw code", () =>
            {
                var e = new LoadStatusEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "OTR_API",
                    StatusType = LoadStatusType.ArrivedAtPickup,
                    SourceStatusCode = "ARRIVED_PICKUP",
                    SourceStatusDescription = "Driver arrived at pickup",
                    StatusTimeUtc = DateTime.UtcNow
                };
                TestHarness.AssertEqual(LoadStatusType.ArrivedAtPickup, e.StatusType);
                TestHarness.AssertEqual("ARRIVED_PICKUP", e.SourceStatusCode);
            });

            TestHarness.Run("LoadAssignedEvent with carrier/driver/equipment", () =>
            {
                var e = new LoadAssignedEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "OTR_API",
                    Carrier = new CarrierInfo { Scac = "ABCD", Name = "Acme Trucking" },
                    Driver = new DriverInfo { Name = "Jane Doe", Phone = "+1-555-0100" },
                    Equipment = new EquipmentInfo { TruckNumber = "T-101", TrailerNumber = "TR-555" }
                };
                TestHarness.AssertEqual("ABCD", e.Carrier.Scac);
                TestHarness.AssertEqual("T-101", e.Equipment.TruckNumber);
            });

            TestHarness.Run("DocumentAvailableEvent carries content bytes", () =>
            {
                var bytes = new byte[] { 1, 2, 3, 4, 5 };
                var e = new DocumentAvailableEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "POD_App",
                    DocumentType = DocumentType.ProofOfDelivery,
                    FileName = "pod-12345.pdf",
                    MimeType = "application/pdf",
                    Content = bytes,
                    CapturedUtc = DateTime.UtcNow
                };
                TestHarness.AssertEqual(5, e.Content.Length);
                TestHarness.AssertEqual(DocumentType.ProofOfDelivery, e.DocumentType);
            });

            TestHarness.Run("LoadTrackingStoppedEvent with reason", () =>
            {
                var e = new LoadTrackingStoppedEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "OTR_API",
                    Reason = "DISPATCHER_STOPPED"
                };
                TestHarness.AssertEqual("DISPATCHER_STOPPED", e.Reason);
            });

            TestHarness.Run("GenericLoadEvent with arbitrary data bag", () =>
            {
                var e = new GenericLoadEvent
                {
                    VectorLoadId = "LOAD12345",
                    SourceSystem = "VectorFBS",
                    EventName = "RateConfirmed",
                    Data = new Dictionary<string, object>
                    {
                        ["rate"] = 1850.00,
                        ["carrier"] = "ABCD"
                    }
                };
                TestHarness.AssertEqual("RateConfirmed", e.EventName);
                TestHarness.AssertEqual(2, e.Data.Count);
            });

            TestHarness.Run("All event types serialize to JSON cleanly (Newtonsoft)", () =>
            {
                VendorEvent[] events =
                {
                    new LoadCreatedEvent { VectorLoadId = "L1", SourceSystem = "T", Mode = "TL" },
                    new LoadAssignedEvent { VectorLoadId = "L1", SourceSystem = "T",
                        Carrier = new CarrierInfo { Scac = "ABCD" } },
                    new LocationReportedEvent { VectorLoadId = "L1", SourceSystem = "T",
                        Latitude = "1", Longitude = "2", LocatedAtUtc = DateTime.UtcNow },
                    new LoadStatusEvent { VectorLoadId = "L1", SourceSystem = "T",
                        StatusType = LoadStatusType.Delivered, StatusTimeUtc = DateTime.UtcNow },
                    new LoadTrackingStoppedEvent { VectorLoadId = "L1", SourceSystem = "T", Reason = "CANCELLED" },
                    new DocumentAvailableEvent { VectorLoadId = "L1", SourceSystem = "T",
                        DocumentType = DocumentType.ProofOfDelivery, FileName = "f.pdf",
                        MimeType = "application/pdf", Content = new byte[] { 1, 2 } },
                    new GenericLoadEvent { VectorLoadId = "L1", SourceSystem = "T", EventName = "x" }
                };
                foreach (var e in events)
                {
                    var json = JsonConvert.SerializeObject(e);
                    TestHarness.Assert(json.Contains("\"VectorLoadId\""),
                        $"JSON for {e.GetType().Name} missing VectorLoadId");
                    TestHarness.Assert(json.Contains("\"L1\""),
                        $"JSON for {e.GetType().Name} missing the load id value");
                }
            });

            // ─── Section 2: VendorOperationResult factories ────────────
            TestHarness.Run("VendorOperationResult.Succeeded sets correct shape", () =>
            {
                var r = VendorOperationResult.Succeeded(202, vendorRequestId: "req-1",
                    expectedCallbackType: "LOAD_CREATION");
                TestHarness.Assert(r.Success, "Success should be true");
                TestHarness.AssertEqual(202, r.HttpStatusCode.Value);
                TestHarness.AssertEqual("req-1", r.VendorRequestId);
                TestHarness.AssertEqual("LOAD_CREATION", r.ExpectedCallbackType);
                TestHarness.AssertNull(r.ErrorCategory, "ErrorCategory");
            });

            TestHarness.Run("VendorOperationResult.Failed(string) sets correct shape", () =>
            {
                var r = VendorOperationResult.Failed("bad payload", "Permanent", httpStatusCode: 400);
                TestHarness.Assert(!r.Success, "Success should be false");
                TestHarness.AssertEqual("Permanent", r.ErrorCategory);
                TestHarness.AssertEqual(400, r.HttpStatusCode.Value);
                TestHarness.AssertEqual("bad payload", r.ErrorMessage);
            });

            TestHarness.Run("VendorOperationResult.Failed(Exception) captures the ex message", () =>
            {
                var ex = new InvalidOperationException("kaboom");
                var r = VendorOperationResult.Failed(ex, "Transient");
                TestHarness.Assert(!r.Success, "Success should be false");
                TestHarness.AssertEqual("Transient", r.ErrorCategory);
                TestHarness.Assert(r.ErrorMessage.Contains("kaboom"), "ErrorMessage should contain ex text");
            });

            TestHarness.Run("VendorOperationResult.Skipped + RateLimited categorize correctly", () =>
            {
                var s = VendorOperationResult.Skipped("not handled");
                TestHarness.AssertEqual("Skipped", s.ErrorCategory);

                var r = VendorOperationResult.RateLimited();
                TestHarness.AssertEqual("RateLimit", r.ErrorCategory);
            });

            // ─── Section 3: Adapter contract is implementable ──────────
            TestHarness.Run("FakeAdapter implements IVendorAdapter cleanly", () =>
            {
                IVendorAdapter adapter = new FakeAdapter();
                TestHarness.AssertEqual("Fake", adapter.VendorName);

                var evt = new LoadAssignedEvent { VectorLoadId = "L1", SourceSystem = "T" };
                TestHarness.Assert(adapter.CanHandle(evt), "Fake should handle LoadAssignedEvent");

                var gen = new GenericLoadEvent { VectorLoadId = "L1", SourceSystem = "T" };
                TestHarness.Assert(!adapter.CanHandle(gen), "Fake should decline GenericLoadEvent");
            });

            TestHarness.Run("FakeAdapter.DispatchAsync returns success result", () =>
            {
                var adapter = new FakeAdapter();
                var profile = new ClientProfile
                {
                    ShipperCode = "VECTOR_DEFAULT",
                    VendorName = "Fake",
                    IsActive = true,
                    EnabledEvents = "LoadAssignedEvent",
                    ConfigJson = "{}"
                };
                var evt = new LoadAssignedEvent { VectorLoadId = "L1", SourceSystem = "T" };
                var result = adapter.DispatchAsync(evt, profile).GetAwaiter().GetResult();

                TestHarness.Assert(result.Success, "Result.Success");
                TestHarness.AssertEqual(202, result.HttpStatusCode.Value);
                TestHarness.AssertNotNull(result.VendorRequestId, "VendorRequestId");
                TestHarness.AssertEqual(1, adapter.DispatchCallCount);
                TestHarness.Assert(ReferenceEquals(evt, adapter.LastEvent), "LastEvent should be the dispatched event");
            });

            TestHarness.Run("FakeInboundProcessor implements IInboundEventProcessor cleanly", () =>
            {
                IInboundEventProcessor p = new FakeInboundProcessor();
                TestHarness.AssertEqual("Fake", p.VendorName);

                var meta = p.ParseAndExtract("{}");
                TestHarness.AssertNotNull(meta, "metadata");
                TestHarness.AssertEqual("FAKE_EVENT", meta.MessageType);
                TestHarness.AssertEqual("LOAD999", meta.VectorLoadId);
            });

            // ─── Section 4: ClientProfile.IsEventEnabled ───────────────
            TestHarness.Run("ClientProfile.IsEventEnabled — exact match", () =>
            {
                var p = new ClientProfile { EnabledEvents = "LoadCreatedEvent,LocationReportedEvent" };
                TestHarness.Assert(p.IsEventEnabled("LoadCreatedEvent"), "exact match");
                TestHarness.Assert(p.IsEventEnabled("LocationReportedEvent"), "exact match 2");
            });

            TestHarness.Run("ClientProfile.IsEventEnabled — case insensitive", () =>
            {
                var p = new ClientProfile { EnabledEvents = "LoadCreatedEvent" };
                TestHarness.Assert(p.IsEventEnabled("loadcreatedevent"), "lowercase");
                TestHarness.Assert(p.IsEventEnabled("LOADCREATEDEVENT"), "uppercase");
            });

            TestHarness.Run("ClientProfile.IsEventEnabled — tolerates whitespace around commas", () =>
            {
                var p = new ClientProfile { EnabledEvents = "A,  B , C " };
                TestHarness.Assert(p.IsEventEnabled("B"), "B should be found despite spaces");
                TestHarness.Assert(p.IsEventEnabled("C"), "C should be found despite trailing space");
            });

            TestHarness.Run("ClientProfile.IsEventEnabled — substring is NOT a match", () =>
            {
                // "LocationReportedEvent" must NOT match a profile enabling "Location"
                var p = new ClientProfile { EnabledEvents = "Location,Status" };
                TestHarness.Assert(!p.IsEventEnabled("LocationReportedEvent"),
                    "substring match must be rejected");
            });

            TestHarness.Run("ClientProfile.IsEventEnabled — empty inputs return false", () =>
            {
                var p = new ClientProfile { EnabledEvents = "" };
                TestHarness.Assert(!p.IsEventEnabled("X"), "empty CSV");
                p.EnabledEvents = "X";
                TestHarness.Assert(!p.IsEventEnabled(""), "empty event name");
                TestHarness.Assert(!p.IsEventEnabled(null), "null event name");
            });

            // ─── Section 5: vendorAdapters config section parses ───────
            TestHarness.Run("vendorAdapters config section loads from App.config", () =>
            {
                var section = VendorAdaptersSection.Load();
                TestHarness.AssertNotNull(section, "section");
                TestHarness.AssertNotNull(section.Adapters, "Adapters collection");
                TestHarness.AssertEqual(2, section.Adapters.Count);
            });

            TestHarness.Run("vendorAdapters lookup by name returns correct element", () =>
            {
                var section = VendorAdaptersSection.Load();
                var fk = section.Adapters["FourKites"];
                TestHarness.AssertNotNull(fk, "FourKites element");
                TestHarness.AssertEqual(
                    "Some.Future.Type.FourKitesAdapter, Vendor.FourKites",
                    fk.AdapterType);
                TestHarness.Assert(fk.InboundProcessorType.Contains("WebhookProcessor"),
                    "inbound processor type set");
                TestHarness.Assert(fk.WebhookValidatorType.Contains("SignatureValidator"),
                    "webhook validator type set");
            });

            TestHarness.Run("vendorAdapters lookup — optional fields can be empty", () =>
            {
                var section = VendorAdaptersSection.Load();
                var p44 = section.Adapters["Project44"];
                TestHarness.AssertNotNull(p44, "Project44 element");
                TestHarness.AssertEqual("", p44.InboundProcessorType);
                TestHarness.AssertEqual("", p44.WebhookValidatorType);
            });

            // ─── Summary ───────────────────────────────────────────────
            return TestHarness.Summarize();
        }
    }
}
