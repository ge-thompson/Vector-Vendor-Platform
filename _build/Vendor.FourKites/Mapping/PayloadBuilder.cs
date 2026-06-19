using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vendor.Common.Events;

namespace Vendor.FourKites.Mapping
{
    /// <summary>
    /// Builds the JSON payloads FourKites expects, per event type.
    /// Rewritten to match the official FK API spec at docs.fourkites.com/api-reference.
    ///
    /// Vector is TL only (no Rail / Ocean / Air). Fields specific to those modes are omitted.
    ///
    /// KEY DIFFERENCES FROM PRE-REWRITE SHAPE:
    ///   - "load" envelope wrapping all load-level fields
    ///   - load.carrier is a String (SCAC), not an object
    ///   - load.haulType is an Array (e.g. ["brokered_load"])
    ///   - Driver + equipment live inside load.trackingInfo, not at top level
    ///   - Stops use stopType ("pickup" | "delivery"), not role enum
    ///   - Stops require: name, addressLine1, city, state,
    ///                    earliestAppointmentTime, latestAppointmentTime
    ///   - Stop datetimes are ISO 8601 in LOCAL TIME (no Z, no offset)
    ///   - billToCode is NOT a Load Create field
    ///   - shipper is NOT a request field (derived from API key on FK's side)
    ///   - Delete payload is just { trackingIds: [int] } — no reason field
    ///   - Document upload uses base64 JSON, not multipart
    ///
    /// All builders return a BuildResult with both the JSON body and a generated requestId
    /// (used for our local audit and for FK's idempotency on subsequent retries).
    /// </summary>
    public static class PayloadBuilder
    {
        public class BuildResult
        {
            public string Json { get; set; }
            public string RequestId { get; set; }
        }

        // ─── Load Create (POST /api/v1/tracking) ─────────────────────────
        // Fires on the first LoadAssignedEvent for a given VectorLoadId.

        public static BuildResult BuildLoadCreate(LoadAssignedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var load = BuildLoadObject(evt, cfg, isUpdate: false);

            var body = new JObject
            {
                ["additionalData"] = new JObject
                {
                    ["modeDetails"] = new JObject
                    {
                        ["shipperModes"] = "TL",
                        ["carrierModes"] = "TL"
                    }
                },
                ["load"] = load
            };

            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── Load Update (PATCH /api/v1/tracking/{fkLoadId}) ─────────────
        // Fires on a subsequent LoadAssignedEvent (driver reassignment, etc.)
        // when LoadCrossReference already has an FK loadId for this VectorLoadId.

        public static BuildResult BuildLoadUpdate(LoadAssignedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();
            var load = BuildLoadObject(evt, cfg, isUpdate: true);

            // simpleUpdate=false ⇒ partial update (only fields included are changed).
            // simpleUpdate=true would mean "treat this as the full snapshot," which we don't want.
            var body = new JObject
            {
                ["simpleUpdate"] = false,
                ["additionalData"] = new JObject
                {
                    ["modeDetails"] = new JObject
                    {
                        ["shipperModes"] = "TL",
                        ["carrierModes"] = "TL"
                    }
                },
                ["load"] = load
            };

            // FK spec: Update can change tracking info, appointment times, driver phone,
            // truck/trailer number even after tracking has started. trackingInfo is the
            // typical Update payload — pull it up to top level for partial Update too.
            var trackingInfo = (JObject)load["trackingInfo"];
            if (trackingInfo != null && trackingInfo.HasValues)
                body["trackingInfo"] = trackingInfo.DeepClone();

            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── Load Delete (POST /api/v1/tracking/delete_loads) ────────────
        // FK delete is batch-capable but our framework fires one event at a time;
        // we send a single-element array. FK accepts up to 100.

        public static BuildResult BuildLoadDelete(long fkLoadId)
        {
            var requestId = Guid.NewGuid().ToString();
            var body = new JObject
            {
                ["trackingIds"] = new JArray(fkLoadId)
            };

            // NOTE: FK does NOT accept a reason field. The LoadTrackingStoppedEvent.Reason
            // (CANCELLED / STOPPED / DELIVERED) is logged in VendorOutboundTransactions
            // but not transmitted to FK — they only learn the load is being stopped.
            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── Document Upload (POST /document-data/upload) ────────────────

        /// <summary>
        /// FK document upload is base64 JSON, not multipart. Max 10 MB per call.
        /// Supported types: PDF, JPEG, TIFF.
        /// </summary>
        public static BuildResult BuildDocumentUpload(DocumentAvailableEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();

            // Map Vector DocumentType -> FK document_type code. Per FK docs:
            //   BLD=Bill of Lading-Driver, PSD=Packing Slip-Driver, BL=Bill of Lading,
            //   PS=Packing Slip, CD=Customer Document, DR=Delivery Receipt/POD,
            //   IV=Invoice, WC=Weight Certificate, WI/WP=Weights, FB=Freight Bill,
            //   PO=Purchase Order, OD=Other Document, FA=Final Approval, LR=Load Report
            // O-008 — confirm with FK CSM which Vector docs map to which codes.
            string fkDocType = MapDocumentType(evt.DocumentType);

            // FK file type codes are lowercase MIME-ish: "pdf", "jpeg", ".tiff"
            string fkFileType = MapFileType(evt.MimeType);

            var body = new JObject
            {
                ["load"] = new JObject
                {
                    ["identifier"] = "loadNumber",
                    ["value"]      = evt.VectorLoadId
                },
                ["documents"] = new JArray
                {
                    new JObject
                    {
                        ["type"]          = fkFileType,
                        ["document_type"] = fkDocType,
                        // PHASE 1 PLACEHOLDER: file bytes aren't yet wired into the event.
                        // When the FBS POD capture path lands, populate base64_content here
                        // from evt.FileContent (or equivalent property).
                        ["base64_content"] = "" // PENDING — see O-008
                    }
                }
            };

            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
        }

        // ─── Shared load-object builder ──────────────────────────────────

        /// <summary>
        /// Builds the FK "load" object for both Create and Update. Same shape; Update will
        /// omit fields it doesn't need to change (simpleUpdate=false ⇒ partial).
        /// </summary>
        private static JObject BuildLoadObject(LoadAssignedEvent evt, FourKitesConfig cfg, bool isUpdate)
        {
            var load = new JObject
            {
                ["loadNumber"] = evt.VectorLoadId,
                // carrier is a STRING (SCAC), not an object. Vector's SCAC from config.
                ["carrier"]    = cfg.VectorScac,
                // haulType is an ARRAY. Default "brokered_load" for Vector.
                ["haulType"]   = new JArray(cfg.DefaultHaulType ?? "brokered_load")
            };

            // Optional: friendly carrier display name (separate from the SCAC identifier)
            if (evt.Carrier != null && !string.IsNullOrWhiteSpace(evt.Carrier.Name))
                load["carrierName"] = evt.Carrier.Name;

            // ExternalLoadId: typically the FBS-side load number; useful for FK's reporting.
            if (!string.IsNullOrEmpty(evt.ExternalLoadId))
                load["displayLoadNumber"] = evt.ExternalLoadId;

            // Reference numbers (PO numbers, etc.) — flattened from stops if present.
            var refNumbers = ExtractReferenceNumbers(evt);
            if (refNumbers.Count > 0)
                load["referenceNumbers"] = new JArray(refNumbers);

            // trackingInfo: driver + equipment all live here per FK spec.
            var trackingInfo = BuildTrackingInfo(evt);
            if (trackingInfo.HasValues)
                load["trackingInfo"] = trackingInfo;

            // Stops: required for Create. For Update, send only if the appointment times
            // or address actually changed (we don't track that today, so send always).
            if (evt.Stops != null && evt.Stops.Count > 0)
                load["stops"] = new JArray(evt.Stops.Select(BuildStop));

            // Load notes: passthrough if set
            if (!string.IsNullOrWhiteSpace(evt.LoadNotes))
                load["tags"] = new JArray(SanitizeTag(evt.LoadNotes));

            return load;
        }

        private static JObject BuildTrackingInfo(LoadAssignedEvent evt)
        {
            var ti = new JObject();

            if (evt.Driver != null)
            {
                // driverPhone: required if FK is using CarrierLink mobile.
                // Format expected: +E.164 (e.g. +13125555555). Vector data may have local
                // format; we pass through unchanged — FK will reject if not E.164.
                if (!string.IsNullOrWhiteSpace(evt.Driver.Phone))
                    ti["driverPhone"] = evt.Driver.Phone;

                // driverId: CarrierLink login id. Vector's "driver name" field is a name,
                // not a login id, so we don't send it as driverId. Leave for future config.
            }

            if (evt.Equipment != null)
            {
                if (!string.IsNullOrWhiteSpace(evt.Equipment.TruckNumber))
                    ti["truckNumber"] = evt.Equipment.TruckNumber;
                if (!string.IsNullOrWhiteSpace(evt.Equipment.TrailerNumber))
                    ti["trailerNumber"] = evt.Equipment.TrailerNumber;
            }

            return ti;
        }

        private static JObject BuildStop(StopInfo stop)
        {
            if (stop == null) return null;

            var jo = new JObject
            {
                // FK requires stopType. Map our enum to FK's string vocabulary.
                ["stopType"]      = MapStopType(stop.Role)
            };

            // Required: name (FK rejects without). Fall back to a sensible default.
            jo["name"] = !string.IsNullOrWhiteSpace(stop.Name)
                ? stop.Name
                : (stop.Role.ToString());

            // Required: addressLine1, city, state. Fail-soft so dev tests still hit FK
            // (FK will reject with a useful error rather than us short-circuiting).
            jo["addressLine1"] = stop.AddressLine1 ?? "";
            jo["city"]         = stop.City ?? "";
            jo["state"]        = stop.State ?? "";

            if (!string.IsNullOrWhiteSpace(stop.PostalCode))
                jo["postalCode"] = stop.PostalCode;
            if (!string.IsNullOrWhiteSpace(stop.Country))
                jo["country"] = stop.Country;

            // FK accepts lat/lon as decimal degrees (decimal, not string). We store strings on
            // StopInfo; parse and emit as decimal so FK doesn't bounce on type.
            if (TryParseDecimal(stop.Latitude, out var lat))   jo["latitude"]  = lat;
            if (TryParseDecimal(stop.Longitude, out var lon))  jo["longitude"] = lon;

            // Required: earliest + latest appointment times. ISO 8601 in stop's LOCAL TIMEZONE,
            // NO Z, NO offset. e.g. "2026-01-15T08:00:00". Fall back to occurredUtc if we don't
            // have a stop-specific time (FK will reject, but we audit the bad payload).
            if (stop.ScheduledArrivalUtc.HasValue)
                jo["earliestAppointmentTime"] = ToFkLocalIso(stop.ScheduledArrivalUtc.Value);
            if (stop.ScheduledDepartureUtc.HasValue)
                jo["latestAppointmentTime"] = ToFkLocalIso(stop.ScheduledDepartureUtc.Value);
            // If only arrival is set, FK still requires latestAppointmentTime — mirror it.
            if (stop.ScheduledArrivalUtc.HasValue && !stop.ScheduledDepartureUtc.HasValue)
                jo["latestAppointmentTime"] = ToFkLocalIso(stop.ScheduledArrivalUtc.Value);

            // stopReferenceId: our external stop id (used by FK to identify "which stop"
            // when posting status callbacks back to us).
            if (!string.IsNullOrWhiteSpace(stop.ExternalStopId))
                jo["stopReferenceId"] = stop.ExternalStopId;

            // sequence: lower = earlier. Use SequenceNumber if set.
            if (stop.SequenceNumber > 0)
                jo["sequence"] = stop.SequenceNumber.ToString();

            return jo;
        }

        // ─── Vocab mappers ───────────────────────────────────────────────

        /// <summary>StopRole -> FK stopType string. TL only — no port/transfer.</summary>
        private static string MapStopType(StopRole role)
        {
            switch (role)
            {
                case StopRole.Pickup:       return "pickup";
                case StopRole.Delivery:     return "delivery";
                case StopRole.Intermediate: return "transfer";   // best fit; FK has "transfer"
                default:                    return "pickup";     // safest default
            }
        }

        private static string MapDocumentType(DocumentType dt)
        {
            // Vector DocumentType enum -> FK document_type code. Confirm via O-008.
            // FK codes: BLD, PSD, BL, PS, CD, DR, IV, WC, WI, WP, FB, PO, OD, FA, LR
            switch (dt)
            {
                case DocumentType.ProofOfDelivery:  return "DR";  // Delivery Receipt
                case DocumentType.BillOfLading:     return "BL";
                case DocumentType.RateConfirmation: return "CD";  // Customer Document
                case DocumentType.WeighSlip:        return "WC";  // Weight Certificate
                default:                            return "OD";  // Other Document
            }
        }

        private static string MapFileType(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType)) return "pdf";
            mimeType = mimeType.ToLowerInvariant();
            if (mimeType.Contains("pdf"))   return "pdf";
            if (mimeType.Contains("jpeg") || mimeType.Contains("jpg")) return "jpeg";
            if (mimeType.Contains("tiff"))  return ".tiff";
            return "pdf";  // default; FK will reject unknown types
        }

        // ─── Misc helpers ────────────────────────────────────────────────

        private static List<string> ExtractReferenceNumbers(LoadAssignedEvent evt)
        {
            var list = new List<string>();
            if (evt.Stops != null)
            {
                foreach (var s in evt.Stops)
                {
                    if (s?.References != null)
                    {
                        foreach (var r in s.References)
                        {
                            if (!string.IsNullOrWhiteSpace(r?.Value)) list.Add(r.Value);
                        }
                    }
                }
            }
            return list;
        }

        private static string SanitizeTag(string s)
        {
            // FK limits a single tag to 50 chars; total <=255 recommended.
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= 50 ? s : s.Substring(0, 50);
        }

        /// <summary>
        /// Formats a DateTime as ISO 8601 with NO Z, NO offset — FK expects appointment
        /// times in the stop's LOCAL TIMEZONE without zone marker.
        /// Example: "2026-01-15T08:00:00".
        ///
        /// NOTE: our StopInfo currently stores ScheduledArrival/Departure as UTC. Without
        /// a stop-side timezone we can't truly localize. For now we emit the UTC clock
        /// reading without the Z, which matches what TT typically sends. Future fix:
        /// add Timezone to StopInfo and convert here.
        /// </summary>
        private static string ToFkLocalIso(DateTime utc)
        {
            // Strip Z / offset by formatting without the K specifier.
            return utc.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        private static bool TryParseDecimal(string s, out decimal value)
        {
            return decimal.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }
    }
}
