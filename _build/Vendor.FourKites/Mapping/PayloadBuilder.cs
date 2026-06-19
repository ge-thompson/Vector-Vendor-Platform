using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vendor.Common.Events;

namespace Vendor.FourKites.Mapping
{
    /// <summary>
    /// Builds the JSON payloads FourKites expects, per event type.
    ///
    /// Each Build* method returns a JSON string ready to put in an HTTP body.
    /// The output shapes are based on FK's documented API — refine as we get
    /// real production feedback (O-002 + future open items on payload shape).
    ///
    /// All builders include:
    ///   - billToCode (from config)
    ///   - loadNumber = VectorLoadId (the universal correlation key, D-008)
    ///   - requestId = freshly-generated GUID (used by FK for idempotency on their side)
    ///   - eventTimestamp = OccurredUtc in ISO 8601
    ///
    /// PHASE 1 CAVEAT: these payload shapes match common FK conventions but
    /// haven't been pressure-tested against the live FK API. Expect some
    /// adjustments after the first round of production traffic. The structure
    /// of this class (one builder per event type, each independent) is designed
    /// to make those adjustments low-risk.
    /// </summary>
    public static class PayloadBuilder
    {
        /// <summary>Result of payload building — both the JSON and the generated requestId.</summary>
        public class BuildResult
        {
            public string Json { get; set; }
            public string RequestId { get; set; }
        }

        // ─── LoadCreatedEvent ────────────────────────────────────────────────

        public static BuildResult BuildLoadCreated(LoadCreatedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc),
                ["mode"]           = evt.Mode,
                ["equipmentType"]  = evt.EquipmentType
            };

            if (evt.Weight.HasValue)
            {
                jo["weight"]     = evt.Weight.Value;
                jo["weightUnit"] = evt.WeightUnit ?? "LB";
            }

            if (evt.Origin != null)      jo["origin"] = BuildStop(evt.Origin);
            if (evt.Destination != null) jo["destination"] = BuildStop(evt.Destination);

            if (evt.Stops != null && evt.Stops.Count > 0)
                jo["stops"] = new JArray(evt.Stops.Select(BuildStop));

            if (evt.References != null && evt.References.Count > 0)
                jo["references"] = new JArray(evt.References.Select(r => new JObject
                {
                    ["type"]  = r.Type,
                    ["value"] = r.Value
                }));

            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── LoadAssignedEvent ───────────────────────────────────────────────

        public static BuildResult BuildLoadAssigned(LoadAssignedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc)
            };

            if (!string.IsNullOrEmpty(evt.ExternalLoadId))
                jo["externalLoadId"] = evt.ExternalLoadId;
            if (!string.IsNullOrEmpty(evt.LoadType))
                jo["loadType"] = evt.LoadType;
            if (!string.IsNullOrEmpty(evt.TrailerType))
                jo["trailerType"] = evt.TrailerType;
            if (!string.IsNullOrEmpty(evt.LoadNotes))
                jo["loadNotes"] = evt.LoadNotes;
            if (evt.IsTeamLoad.HasValue)
                jo["isTeamLoad"] = evt.IsTeamLoad.Value;

            if (evt.Carrier != null)
            {
                jo["carrier"] = new JObject
                {
                    ["scac"]         = evt.Carrier.Scac,
                    ["name"]         = evt.Carrier.Name,
                    ["mcNumber"]     = evt.Carrier.McNumber,
                    ["dotNumber"]    = evt.Carrier.DotNumber
                };
            }

            if (evt.Driver != null)
            {
                jo["driver"] = new JObject
                {
                    ["name"]       = evt.Driver.Name,
                    ["phone"]      = evt.Driver.Phone,
                    ["email"]      = evt.Driver.Email,
                    ["driverType"] = evt.Driver.DriverType,
                    ["comments"]   = evt.Driver.Comments
                };
            }

            if (evt.Equipment != null)
            {
                jo["equipment"] = new JObject
                {
                    ["truckNumber"]   = evt.Equipment.TruckNumber,
                    ["trailerNumber"] = evt.Equipment.TrailerNumber,
                    ["trailerType"]   = evt.Equipment.TrailerType,
                    ["vin"]           = evt.Equipment.Vin,
                    ["licensePlate"]  = evt.Equipment.LicensePlate
                };
            }

            if (evt.Dispatcher != null)
            {
                jo["dispatcher"] = new JObject
                {
                    ["id"]    = evt.Dispatcher.Id,
                    ["name"]  = evt.Dispatcher.Name,
                    ["email"] = evt.Dispatcher.Email,
                    ["phone"] = evt.Dispatcher.Phone
                };
            }

            if (evt.Shipper != null)
            {
                jo["shipper"] = new JObject
                {
                    ["shipperId"]          = evt.Shipper.ShipperId,
                    ["referenceNumber"]    = evt.Shipper.ReferenceNumber,
                    ["notificationEmails"] = evt.Shipper.NotificationEmails
                };
            }

            if (evt.Stops != null && evt.Stops.Count > 0)
                jo["stops"] = new JArray(evt.Stops.Select(BuildStop));

            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── LocationReportedEvent ───────────────────────────────────────────

        public static BuildResult BuildLocationReported(LocationReportedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc),
                ["latitude"]       = evt.Latitude,
                ["longitude"]      = evt.Longitude,
                ["locatedAt"]      = ToIso(evt.LocatedAtUtc),
                ["city"]           = evt.City,
                ["state"]          = evt.State,
                ["country"]        = evt.Country
            };

            if (evt.SpeedMph.HasValue) jo["speedMph"] = evt.SpeedMph.Value;
            if (evt.Heading.HasValue)  jo["heading"]  = evt.Heading.Value;

            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── LoadStatusEvent ─────────────────────────────────────────────────

        public static BuildResult BuildLoadStatus(LoadStatusEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var edi214 = LoadStatusMapper.MapFromEvent(evt);

            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc),
                ["statusCode"]     = edi214,
                ["statusTime"]     = ToIso(evt.StatusTimeUtc),
                ["sourceCode"]     = evt.SourceStatusCode,
                ["description"]    = evt.SourceStatusDescription
            };

            if (evt.AtStop != null) jo["atStop"] = BuildStop(evt.AtStop);

            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── LoadTrackingStoppedEvent ────────────────────────────────────────

        public static BuildResult BuildTrackingStopped(LoadTrackingStoppedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc),
                ["reason"]         = evt.Reason ?? "UNKNOWN"
            };
            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── DocumentAvailableEvent ──────────────────────────────────────────
        // Note: this builder returns ONLY the metadata JSON. The file bytes go
        // in a separate multipart part — the client handles that composition.

        public static BuildResult BuildDocumentMetadata(DocumentAvailableEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var jo = new JObject
            {
                ["billToCode"]     = cfg.BillToCode,
                ["loadNumber"]     = evt.VectorLoadId,
                ["requestId"]      = requestId,
                ["eventTimestamp"] = ToIso(evt.OccurredUtc),
                ["documentType"]   = evt.DocumentType.ToString(),
                ["fileName"]       = evt.FileName,
                ["mimeType"]       = evt.MimeType,
                ["capturedAt"]     = evt.CapturedUtc.HasValue ? ToIso(evt.CapturedUtc.Value) : null
            };
            return new BuildResult { Json = jo.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── Shared helpers ──────────────────────────────────────────────────

        private static JObject BuildStop(StopInfo stop)
        {
            if (stop == null) return null;
            var jo = new JObject
            {
                ["sequence"]          = stop.SequenceNumber,
                ["role"]              = stop.Role.ToString(),
                ["name"]              = stop.Name,
                ["addressLine1"]      = stop.AddressLine1,
                ["city"]              = stop.City,
                ["state"]             = stop.State,
                ["postalCode"]        = stop.PostalCode,
                ["country"]           = stop.Country,
                ["latitude"]          = stop.Latitude,
                ["longitude"]         = stop.Longitude,
                ["notes"]             = stop.Notes,
                ["externalStopId"]    = stop.ExternalStopId,
                ["scheduledArrival"]  = stop.ScheduledArrivalUtc.HasValue ? ToIso(stop.ScheduledArrivalUtc.Value) : null,
                ["scheduledDeparture"]= stop.ScheduledDepartureUtc.HasValue ? ToIso(stop.ScheduledDepartureUtc.Value) : null
            };

            if (stop.References != null && stop.References.Count > 0)
                jo["references"] = new JArray(stop.References.Select(r => new JObject
                {
                    ["type"]  = r.Type,
                    ["value"] = r.Value
                }));

            return jo;
        }

        private static string ToIso(DateTime utc)
        {
            // FK accepts standard ISO 8601 with Z suffix. Use round-trip format for safety.
            return utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
    }
}
