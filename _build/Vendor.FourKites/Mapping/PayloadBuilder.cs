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

        // ─── Load Create from LoadCreatedEvent (POST /api/v1/tracking) ───
        // Fires when FBS creates a load from a customer order (EDI 204). No driver yet —
        // trackingInfo (driverPhone, truck #, trailer #) will arrive later via
        // LoadAssignedEvent (Update path).

        public static BuildResult BuildLoadCreateFromCreated(LoadCreatedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();

            var load = new JObject
            {
                ["loadNumber"] = evt.VectorLoadId,
                ["carrier"]    = cfg.VectorScac,
                ["haulType"]   = new JArray(cfg.DefaultHaulType ?? "brokered_load")
            };

            // Reference numbers (PO, BOL, etc.) come straight off the event.
            if (evt.References != null && evt.References.Count > 0)
            {
                var refs = evt.References
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Value))
                    .Select(r => r.Value)
                    .ToList();
                if (refs.Count > 0)
                    load["referenceNumbers"] = new JArray(refs);
            }

            // Stops: prefer Stops[] if populated, else fall back to Origin + Destination.
            List<StopInfo> stops = evt.Stops;
            if (stops == null || stops.Count == 0)
            {
                stops = new List<StopInfo>();
                if (evt.Origin != null)      stops.Add(evt.Origin);
                if (evt.Destination != null) stops.Add(evt.Destination);
            }

            if (stops.Count > 0)
                load["stops"] = new JArray(stops.Select(BuildStop));

            var body = new JObject
            {
                ["additionalData"] = new JObject
                {
                    ["modeDetails"] = new JObject
                    {
                        ["shipperModes"] = evt.Mode ?? "TL",
                        ["carrierModes"] = evt.Mode ?? "TL"
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

        // ─── Dispatcher Update (POST /load/update/dispatcher-api/async) ──
        // Single endpoint that carries locationUpdate and/or eventUpdate sub-objects.
        // Used by both LocationReportedEvent (locationUpdate only) and LoadStatusEvent
        // (eventUpdate only). When both arrive in the same TT callback we COULD batch
        // into one call — the framework currently dispatches separately so each event
        // becomes its own audit row, which is the right tradeoff for Phase 1 visibility.

        public static BuildResult BuildDispatcherLocation(LocationReportedEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();

            var locationUpdate = new JObject
            {
                // FK requires lat/lon as STRINGS in Decimal Degrees format
                ["latitude"]  = evt.Latitude  ?? "",
                ["longitude"] = evt.Longitude ?? "",
                // ISO 8601 with Z suffix (different from stop appointment times which omit Z)
                ["locatedAt"] = ToFkUtcIso(evt.LocatedAtUtc)
            };
            if (!string.IsNullOrWhiteSpace(evt.City))  locationUpdate["city"]  = evt.City;
            if (!string.IsNullOrWhiteSpace(evt.State)) locationUpdate["state"] = evt.State;

            var body = BuildDispatcherEnvelope(evt.ShipmentNumber, cfg, locationUpdate: locationUpdate, eventUpdate: null);
            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
        }

        public static BuildResult BuildDispatcherStatus(LoadStatusEvent evt, FourKitesConfig cfg)
        {
            var requestId = Guid.NewGuid().ToString();

            // ─── ORIGINAL: always-eventUpdate path (preserved for reference) ─────
            // The original implementation always built an eventUpdate sub-object regardless
            // of whether the LoadStatusEvent carried a milestone (X1/AF/X3/...) or an
            // appointment change. For appointment changes that approach loses the actual
            // new times since eventUpdate has no slots for earliestAppointmentTime /
            // latestAppointmentTime — those live on stopUpdate (Part 1.4 of FK spec).
            /*
            // Use the same status mapper that drives LoadStatusEvent codes — the FK template
            // maps LoadStatusType to EDI 214 codes (X1/AF/X3/CD/D1/OA/X9). Keep it.
            var statusCode = LoadStatusMapper.MapFromEvent(evt);

            var eventUpdate = new JObject
            {
                ["statusCode"]     = statusCode,
                ["eventTimeStamp"] = ToFkUtcIso(evt.StatusTimeUtc)
            };
            if (!string.IsNullOrWhiteSpace(evt.SourceStatusDescription))
                eventUpdate["statusDescription"] = evt.SourceStatusDescription;
            if (!string.IsNullOrWhiteSpace(evt.SourceStatusCode))
                eventUpdate["statusReasonCode"] = evt.SourceStatusCode;
            // "delivered" flag: only set true when the status is Delivered so FK can flip the load
            if (evt.StatusType == LoadStatusType.Delivered)
                eventUpdate["delivered"] = true;

            var body = BuildDispatcherEnvelope(evt.VectorLoadId, cfg, locationUpdate: null, eventUpdate: eventUpdate);
            return new BuildResult { Json = body.ToString(Formatting.None), RequestId = requestId };
            */

            // ─── NEW: branch by event semantic ──────────────────────────────────
            // Appointment changes (SourceStatusDescription == "AppointmentChanged") route to
            // stopUpdate (sibling of loadUpdate) carrying the new earliestAppointmentTime /
            // latestAppointmentTime. Address fields on AtStop are NOT sendable via the
            // Dispatcher API — they would require the Manage Loads PATCH endpoint (license).
            // They still flow through OTR's audit row for visibility.
            // Everything else continues to route to eventUpdate inside loadUpdate[].
            bool isAppointmentChange =
                string.Equals(evt.SourceStatusDescription, "AppointmentChanged",
                              StringComparison.OrdinalIgnoreCase);

            if (isAppointmentChange && evt.AtStop != null)
            {
                var stopUpdate = BuildStopUpdateFromStop(evt.AtStop);
                var apptBody = BuildDispatcherEnvelope(
                    evt.ShipmentNumber, cfg,
                    locationUpdate: null, eventUpdate: null, stopUpdate: stopUpdate);
                return new BuildResult { Json = apptBody.ToString(Formatting.None), RequestId = requestId };
            }

            // Default: status milestone → eventUpdate (existing behavior, unchanged)
            var statusCode = LoadStatusMapper.MapFromEvent(evt);

            var milestoneEventUpdate = new JObject
            {
                ["statusCode"]     = statusCode,
                ["eventTimeStamp"] = ToFkUtcIso(evt.StatusTimeUtc)
            };
            if (!string.IsNullOrWhiteSpace(evt.SourceStatusDescription))
                milestoneEventUpdate["statusDescription"] = evt.SourceStatusDescription;
            if (!string.IsNullOrWhiteSpace(evt.SourceStatusCode))
                milestoneEventUpdate["statusReasonCode"] = evt.SourceStatusCode;
            // "delivered" flag: only set true when the status is Delivered so FK can flip the load
            if (evt.StatusType == LoadStatusType.Delivered)
                milestoneEventUpdate["delivered"] = true;

            var statusBody = BuildDispatcherEnvelope(
                evt.ShipmentNumber, cfg,
                locationUpdate: null, eventUpdate: milestoneEventUpdate, stopUpdate: null);
            return new BuildResult { Json = statusBody.ToString(Formatting.None), RequestId = requestId };
        }

        /// <summary>
        /// Build a stopUpdate sub-object for the Dispatcher API from a StopInfo populated
        /// by an appointment-change event. Per FK spec (Part 1.4), stopUpdate carries the
        /// appointment times (earliestAppointmentTime / latestAppointmentTime) and
        /// identifies the stop via ONE of stopSequence, stopReferenceId, or postalCode.
        ///
        /// Identifier preference: stopReferenceId (most durable) > stopSequence > postalCode.
        /// If none are present FK will reject the update — but the audit row still captures
        /// the bad payload for diagnosis.
        ///
        /// NOTE: stopUpdate has NO fields for address changes (addressLine1/city/state/lat/long).
        /// Those require the Manage Loads PATCH /api/v1/tracking/{id}?simpleUpdate=true
        /// endpoint, which is gated on the Core Track / Appointment Manager license. Address
        /// fields on AtStop still flow into OTR's audit row for visibility — they're just not
        /// sent on the wire to FK from this path.
        /// </summary>
        private static JObject BuildStopUpdateFromStop(StopInfo stop)
        {
            var su = new JObject();

            // ─── Identifier (at least ONE is required per FK spec) ───
            bool hasIdentifier = false;
            if (!string.IsNullOrWhiteSpace(stop.ExternalStopId))
            {
                su["stopReferenceId"] = stop.ExternalStopId;
                hasIdentifier = true;
            }
            if (stop.SequenceNumber.HasValue && stop.SequenceNumber.Value > 0)
            {
                su["stopSequence"] = stop.SequenceNumber.Value.ToString();
                hasIdentifier = true;
            }
            if (!hasIdentifier && !string.IsNullOrWhiteSpace(stop.PostalCode))
            {
                su["postalCode"] = stop.PostalCode;
            }

            // ─── Stop type (optional but informative — pickup/delivery) ───
            var stopTypeStr = MapStopType(stop.Role);
            if (!string.IsNullOrWhiteSpace(stopTypeStr))
                su["stopType"] = stopTypeStr;

            // ─── Appointment window — the actual point of this update ───
            // ISO 8601 with Z suffix. The envelope sets timeZone="UTC" so FK interprets
            // these as UTC instants. Future fix: if StopInfo gains a timezone, convert
            // here to the stop's local wall-clock and drop the envelope timeZone.
            if (stop.ScheduledArrivalUtc.HasValue)
                su["earliestAppointmentTime"] = ToFkUtcIso(stop.ScheduledArrivalUtc.Value);
            if (stop.ScheduledDepartureUtc.HasValue)
                su["latestAppointmentTime"] = ToFkUtcIso(stop.ScheduledDepartureUtc.Value);
            // If only arrival is set, mirror it — FK collapses earliest==latest to a single time.
            if (stop.ScheduledArrivalUtc.HasValue && !stop.ScheduledDepartureUtc.HasValue)
                su["latestAppointmentTime"] = ToFkUtcIso(stop.ScheduledArrivalUtc.Value);

            return su;
        }

        /// <summary>
        /// Shared envelope builder for the Dispatcher Update endpoint. Produces:
        ///   { "updates": [{ "billToCode": ..., "identifierKeys": [...],
        ///                    "stopUpdate": { ... },               // optional, sibling of loadUpdate
        ///                    "loadUpdate": [{ ... }] }] }
        /// Caller passes in whichever sub-object(s) apply (locationUpdate, eventUpdate, stopUpdate).
        ///
        /// CRITICAL: stopUpdate sits at update[].stopUpdate — a SIBLING of loadUpdate, NOT
        /// inside loadUpdate[]. Putting it inside loadUpdate[] is silently wrong (FK accepts
        /// the payload but never applies the appointment change).
        /// </summary>
        private static JObject BuildDispatcherEnvelope(
            string shipmentNumber,
            FourKitesConfig cfg,
            JObject locationUpdate,
            JObject eventUpdate,
            JObject stopUpdate = null)
        {
            // ─── ORIGINAL envelope (loadUpdate-only — no stopUpdate slot) ─────────
            /*
            // Identify the load by Vector's loadNumber. billToCode scopes the match to
            // the right shipper. FK matches on identifierKeys[0] first, then [1], etc.
            var identifierKey = new JObject
            {
                ["identifier"]     = vectorLoadId,
                ["identifierType"] = "loadNumber"
            };

            var loadUpdate = new JObject();
            if (locationUpdate != null) loadUpdate["locationUpdate"] = locationUpdate;
            if (eventUpdate    != null) loadUpdate["eventUpdate"]    = eventUpdate;

            var update = new JObject
            {
                ["timeZone"]       = "UTC",
                ["billToCode"]     = cfg.BillToCode,
                ["identifierKeys"] = new JArray(identifierKey),
                ["loadUpdate"]     = new JArray(loadUpdate)
            };

            return new JObject
            {
                ["updates"] = new JArray(update)
            };
            */

            // ─── NEW envelope — adds stopUpdate at the sibling level ─────────────
            // Identifier is ALWAYS the customer's shipmentNumber (Tracking.ShipmentID /
            // MasterBOL on FBS). Vector's internal LoadID is intentionally NOT used here
            // because FK loads are created by the customer using their own shipmentID;
            // FK doesn't know Vector's number. Empty shipmentNumber will cause FK to
            // reject the payload — that rejection surfaces in VendorOutboundTransactions
            // as a clean signal that the Tracking row is missing ShipmentID.
            var identifierKey = new JObject
            {
                ["identifier"]     = shipmentNumber ?? "",
                ["identifierType"] = "loadNumber"
            };

            var update = new JObject
            {
                ["timeZone"]       = "UTC",
                ["billToCode"]     = cfg.BillToCode,
                ["identifierKeys"] = new JArray(identifierKey)
            };

            // stopUpdate is a SIBLING of loadUpdate at update[].* — appointment-only path.
            if (stopUpdate != null)
                update["stopUpdate"] = stopUpdate;

            // loadUpdate carries locationUpdate and/or eventUpdate when present.
            // Omit the loadUpdate array entirely if neither is supplied so a stopUpdate-only
            // payload doesn't ship an empty loadUpdate that confuses FK.
            if (locationUpdate != null || eventUpdate != null)
            {
                var loadUpdate = new JObject();
                if (locationUpdate != null) loadUpdate["locationUpdate"] = locationUpdate;
                if (eventUpdate    != null) loadUpdate["eventUpdate"]    = eventUpdate;
                update["loadUpdate"] = new JArray(loadUpdate);
            }

            return new JObject
            {
                ["updates"] = new JArray(update)
            };
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
                    // Use the customer's shipmentNumber as FK's identifier — same rule as
                    // the Dispatcher API path. FK doesn't know VectorLoadId.
                    ["value"]      = evt.ShipmentNumber ?? ""
                },
                ["documents"] = new JArray
                {
                    new JObject
                    {
                        ["type"]          = fkFileType,
                        ["document_type"] = fkDocType,
                        ["base64_content"] = evt.Content != null && evt.Content.Length > 0
                            ? Convert.ToBase64String(evt.Content)
                            : ""
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

            // FK accepts lat/lon as decimal degrees in STRING form (per spec:
            // "The only accepted format of lat/long by FOURKITES is the 'Decimal Degrees' format").
            if (!string.IsNullOrWhiteSpace(stop.Latitude))   jo["latitude"]  = stop.Latitude;
            if (!string.IsNullOrWhiteSpace(stop.Longitude))  jo["longitude"] = stop.Longitude;

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

        /// <summary>
        /// Formats a DateTime as ISO 8601 with Z suffix — FK's Dispatcher Update endpoint
        /// expects timestamps in UTC with the Z marker (e.g. "2026-06-19T22:15:05Z").
        /// Different from stop appointment times which omit the Z.
        /// </summary>
        private static string ToFkUtcIso(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ");
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
