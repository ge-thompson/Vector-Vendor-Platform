using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json;
using OTR_API.Filters;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Events;
using Vendor.Common.Persistence;

namespace OTR_API.Controllers
{
    /// <summary>
    /// VVI dispatch entry point. Receives a vendor-agnostic event from a caller (FBS via
    /// the InMotion SDK, or a test harness), looks up the active VVIProfiles for the
    /// load's customer + event, and fans out to each configured vendor adapter.
    ///
    /// This is the consumer side of the VVIProfiles model: profile rows in (read by
    /// VVIProfileRepository), vendor API calls out (via the adapter resolved by
    /// AdapterName). Zero rows for a customer/event is a valid no-op.
    ///
    /// DispatchStatus is the first wired path — sends a status / check call (location +
    /// status) to every vendor whose profile has CheckCall enabled for that customer.
    /// </summary>
    //[HMACAuthentication]
    [RoutePrefix("api/vendordispatch")]
    public class VendorDispatchController : ApiController
    {
        // ─── Request payloads (all generic Vector data — no vendor shaping) ───

        /// <summary>Status / check call (location + optional milestone).</summary>
        public class VVIStatusRequest
        {
            public int CustomerID { get; set; }
            public string VectorLoadId { get; set; }
            public string Latitude { get; set; }
            public string Longitude { get; set; }
            public string City { get; set; }
            public string State { get; set; }
            public string StatusCode { get; set; }   // optional milestone (e.g. "X3")
            public DateTime? OccurredUtc { get; set; }
        }

        /// <summary>Load posted / ready. Generic load detail; adapter shapes per vendor.</summary>
        public class VVIPostLoadRequest
        {
            public int CustomerID { get; set; }
            public string VectorLoadId { get; set; }
            public string Mode { get; set; }            // "TL", "LTL", etc. (optional)
            public string EquipmentType { get; set; }   // "Dry Van", etc. (optional)
            public decimal? Weight { get; set; }
            public string WeightUnit { get; set; }       // "LB" / "KG" (optional)
            public List<VVIStop> Stops { get; set; }     // ordered stops (optional)
            public DateTime? OccurredUtc { get; set; }
        }

        /// <summary>Cancel / stop a load at the vendor.</summary>
        public class VVICancelRequest
        {
            public int CustomerID { get; set; }
            public string VectorLoadId { get; set; }
            public string Reason { get; set; }           // optional free text
            public DateTime? OccurredUtc { get; set; }
        }

        /// <summary>Appointment change for a stop.</summary>
        public class VVIAppointmentRequest
        {
            public int CustomerID { get; set; }
            public string VectorLoadId { get; set; }
            public string StopExternalId { get; set; }       // which stop (optional)
            public DateTime? ScheduledArrivalUtc { get; set; }
            public DateTime? ScheduledDepartureUtc { get; set; }
            public string City { get; set; }
            public string State { get; set; }
            public DateTime? OccurredUtc { get; set; }
        }

        /// <summary>A document (POD, BOL, etc.) to deliver to the vendor.</summary>
        public class VVIPodRequest
        {
            public int CustomerID { get; set; }
            public string VectorLoadId { get; set; }
            public string DocumentType { get; set; }     // "ProofOfDelivery", "BillOfLading", ... (optional)
            public string FileName { get; set; }
            public string MimeType { get; set; }
            public string ContentBase64 { get; set; }    // file bytes, base64-encoded
            public DateTime? CapturedUtc { get; set; }
            public DateTime? OccurredUtc { get; set; }
        }

        /// <summary>A generic stop on a posted load.</summary>
        public class VVIStop
        {
            public int? SequenceNumber { get; set; }
            public string Role { get; set; }             // "Pickup" / "Delivery" / "Intermediate"
            public string Name { get; set; }
            public string AddressLine1 { get; set; }
            public string City { get; set; }
            public string State { get; set; }
            public string PostalCode { get; set; }
            public string Latitude { get; set; }
            public string Longitude { get; set; }
            public DateTime? ScheduledArrivalUtc { get; set; }
            public DateTime? ScheduledDepartureUtc { get; set; }
        }

        /// <summary>One vendor's dispatch outcome, returned to the caller for visibility.</summary>
        public class VVIDispatchResult
        {
            public string Vendor { get; set; }
            public string AdapterName { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }
            public int? HttpStatusCode { get; set; }
            public string VendorRequestId { get; set; }
            public string VendorLoadId { get; set; }
            public string ResponseBody { get; set; }
            public long TransactionId { get; set; }   // audit row id in VendorOutboundTransactions
        }

        // http://localhost:5129/api/vendordispatch/status
        [HttpPost, Route("status")]
        public Task<HttpResponseMessage> DispatchStatus([FromBody] VVIStatusRequest req)
        {
            if (req == null || req.CustomerID <= 0)
                return BadRequest("Status");

            var evt = new LocationReportedEvent
            {
                VectorLoadId = req.VectorLoadId,
                SourceSystem = "OTR_API",
                OccurredUtc  = req.OccurredUtc ?? DateTime.UtcNow,
                Latitude     = req.Latitude,
                Longitude    = req.Longitude,
                City         = req.City,
                State        = req.State,
                LocatedAtUtc = req.OccurredUtc ?? DateTime.UtcNow
            };

            return Dispatch(req.CustomerID, "CheckCall", evt, "Status");
        }

        // http://localhost:5129/api/vendordispatch/postload
        [HttpPost, Route("postload")]
        public Task<HttpResponseMessage> DispatchPostLoad([FromBody] VVIPostLoadRequest req)
        {
            if (req == null || req.CustomerID <= 0)
                return BadRequest("PostLoad");

            var evt = new LoadCreatedEvent
            {
                VectorLoadId  = req.VectorLoadId,
                SourceSystem  = "OTR_API",
                OccurredUtc   = req.OccurredUtc ?? DateTime.UtcNow,
                Mode          = req.Mode,
                EquipmentType = req.EquipmentType,
                Weight        = req.Weight,
                WeightUnit    = req.WeightUnit,
                Stops         = MapStops(req.Stops)
            };

            return Dispatch(req.CustomerID, "LoadPosted", evt, "PostLoad");
        }

        // http://localhost:5129/api/vendordispatch/cancel
        [HttpPost, Route("cancel")]
        public Task<HttpResponseMessage> DispatchCancel([FromBody] VVICancelRequest req)
        {
            if (req == null || req.CustomerID <= 0)
                return BadRequest("Cancel");

            var evt = new LoadTrackingStoppedEvent
            {
                VectorLoadId = req.VectorLoadId,
                SourceSystem = "OTR_API",
                OccurredUtc  = req.OccurredUtc ?? DateTime.UtcNow,
                Reason       = string.IsNullOrWhiteSpace(req.Reason) ? "CANCELLED" : req.Reason
            };

            return Dispatch(req.CustomerID, "CancelLoad", evt, "Cancel");
        }

        // http://localhost:5129/api/vendordispatch/appointment
        [HttpPost, Route("appointment")]
        public Task<HttpResponseMessage> DispatchAppointment([FromBody] VVIAppointmentRequest req)
        {
            if (req == null || req.CustomerID <= 0)
                return BadRequest("Appointment");

            // No dedicated appointment event yet; carry the change as a status event with
            // the new appointment window on the stop. Adapters that support appointment
            // updates translate from the stop's scheduled times.
            var evt = new LoadStatusEvent
            {
                VectorLoadId  = req.VectorLoadId,
                SourceSystem  = "OTR_API",
                OccurredUtc   = req.OccurredUtc ?? DateTime.UtcNow,
                StatusType    = LoadStatusType.Other,
                StatusTimeUtc = req.OccurredUtc ?? DateTime.UtcNow,
                SourceStatusDescription = "AppointmentChanged",
                AtStop = new StopInfo
                {
                    ExternalStopId        = req.StopExternalId,
                    City                  = req.City,
                    State                 = req.State,
                    ScheduledArrivalUtc   = req.ScheduledArrivalUtc,
                    ScheduledDepartureUtc = req.ScheduledDepartureUtc
                }
            };

            return Dispatch(req.CustomerID, "AppointmentChanged", evt, "Appointment");
        }

        // http://localhost:5129/api/vendordispatch/pod
        [HttpPost, Route("pod")]
        public Task<HttpResponseMessage> DispatchPod([FromBody] VVIPodRequest req)
        {
            if (req == null || req.CustomerID <= 0)
                return BadRequest("Pod");

            byte[] content = null;
            if (!string.IsNullOrWhiteSpace(req.ContentBase64))
            {
                try { content = Convert.FromBase64String(req.ContentBase64); }
                catch { content = null; }
            }

            DocumentType docType;
            if (!Enum.TryParse(req.DocumentType, true, out docType))
                docType = DocumentType.ProofOfDelivery;

            var evt = new DocumentAvailableEvent
            {
                VectorLoadId = req.VectorLoadId,
                SourceSystem = "OTR_API",
                OccurredUtc  = req.OccurredUtc ?? DateTime.UtcNow,
                DocumentType = docType,
                FileName     = req.FileName,
                MimeType     = req.MimeType,
                Content      = content,
                CapturedUtc  = req.CapturedUtc
            };

            return Dispatch(req.CustomerID, "POD", evt, "Pod");
        }

        // ─── Shared dispatch engine ──────────────────────────────────────────

        /// <summary>
        /// The one fan-out path every event uses: read active profiles for the customer
        /// + event flag, then for each profile resolve the adapter, send the event, and
        /// audit the outcome. Returns a per-vendor result list. Zero profiles = empty 200.
        /// </summary>
        private async Task<HttpResponseMessage> Dispatch(
            int customerId, string eventFlag, VendorEvent evt, string source)
        {
            var da = new OTR_API.DataClasses.DataAudit();
            var results = new List<VVIDispatchResult>();

            if (!VendorDispatcher.IsConfigured)
            {
                da.InsertErrorAuditLog("VendorDispatcher not configured.", "VendorDispatch." + source);
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable, results);
            }

            var dispatcher = VendorDispatcher.Instance;
            var registry   = dispatcher.Registry;
            var connStr    = dispatcher.AuditConnectionString;

            var repo = new VVIProfileRepository(connStr, ex => da.InsertErrorAuditLog(ex.ToString(), "VendorDispatch.ProfileRead"));
            var profiles = repo.GetActiveProfiles(customerId, eventFlag);

            var audit = new OutboundTransactionRepository(connStr, ex => da.InsertErrorAuditLog(ex.ToString(), "VendorDispatch.Audit"));

            if (profiles.Count == 0)
            {
                // Fired, but no active profile for this customer + event. Record a SKIPPED
                // row so "events with no destination" are visible, then return empty 200.
                await audit.InsertSkippedAsync(evt, "(none)",
                    "No active VVIProfile for customer " + customerId + " + event " + eventFlag + ".",
                    CancellationToken.None).ConfigureAwait(false);
                return Request.CreateResponse(HttpStatusCode.OK, results);
            }

            foreach (var p in profiles)
            {
                var outcome = new VVIDispatchResult { Vendor = p.Vendor, AdapterName = p.AdapterName };

                try
                {
                    var adapter = registry.GetAdapter(p.AdapterName);
                    if (adapter == null)
                    {
                        outcome.Success = false;
                        outcome.Message = "No adapter registered for AdapterName '" + p.AdapterName + "'.";
                        da.InsertErrorAuditLog(outcome.Message, "VendorDispatch." + source);
                        await audit.InsertSkippedAsync(evt, p.Vendor, outcome.Message, CancellationToken.None).ConfigureAwait(false);
                        results.Add(outcome);
                        continue;
                    }

                    if (!adapter.CanHandle(evt))
                    {
                        outcome.Success = false;
                        outcome.Message = "Adapter does not handle this event type.";
                        await audit.InsertSkippedAsync(evt, p.Vendor, outcome.Message, CancellationToken.None).ConfigureAwait(false);
                        results.Add(outcome);
                        continue;
                    }

                    var clientProfile = BuildClientProfile(p);

                    long txnId = await audit.InsertPendingAsync(evt, p.Vendor, customerId.ToString(), CancellationToken.None).ConfigureAwait(false);

                    var result = await adapter.DispatchAsync(evt, clientProfile, CancellationToken.None).ConfigureAwait(false);

                    await audit.RecordOutcomeAsync(txnId, result, CancellationToken.None).ConfigureAwait(false);

                    outcome.TransactionId   = txnId;
                    outcome.Success         = result != null && result.Success;
                    outcome.HttpStatusCode  = result?.HttpStatusCode;
                    outcome.VendorRequestId = result?.VendorRequestId;
                    outcome.VendorLoadId    = result?.VendorLoadId;
                    outcome.ResponseBody    = result?.ResponseBodyJson;
                    outcome.Message         = outcome.Success ? "OK" : (result?.ErrorMessage ?? "No result returned.");
                }
                catch (Exception ex)
                {
                    outcome.Success = false;
                    outcome.Message = ex.Message;
                    da.InsertErrorAuditLog(ex.ToString(), "VendorDispatch." + source + ".Fanout");
                }

                results.Add(outcome);
            }

            return Request.CreateResponse(HttpStatusCode.OK, results);
        }

        private Task<HttpResponseMessage> BadRequest(string source)
        {
            new OTR_API.DataClasses.DataAudit()
                .InsertErrorAuditLog("Null or invalid request (CustomerID required).", "VendorDispatch." + source);
            return Task.FromResult(Request.CreateResponse(HttpStatusCode.BadRequest, new List<VVIDispatchResult>()));
        }

        private static List<StopInfo> MapStops(List<VVIStop> stops)
        {
            if (stops == null || stops.Count == 0) return null;
            var list = new List<StopInfo>();
            foreach (var s in stops)
            {
                StopRole role;
                if (!Enum.TryParse(s.Role, true, out role)) role = StopRole.Intermediate;
                list.Add(new StopInfo
                {
                    SequenceNumber        = s.SequenceNumber,
                    Role                  = role,
                    Name                  = s.Name,
                    AddressLine1          = s.AddressLine1,
                    City                  = s.City,
                    State                 = s.State,
                    PostalCode            = s.PostalCode,
                    Latitude              = s.Latitude,
                    Longitude             = s.Longitude,
                    ScheduledArrivalUtc   = s.ScheduledArrivalUtc,
                    ScheduledDepartureUtc = s.ScheduledDepartureUtc
                });
            }
            return list;
        }

        /// <summary>
        /// Projects a VVIProfile row into a ClientProfile whose ConfigJson carries the
        /// vendor-specific settings the adapter parses. The VVIProfile stores endpoint and
        /// credentials in dedicated columns; we assemble them into the JSON shape the
        /// adapter already knows how to read.
        /// </summary>
        private static ClientProfile BuildClientProfile(VVIProfile p)
        {
            // Minimal config the FK adapter needs: apiKey + billToCode (+ optional endpoint).
            // Instructions, if present, may carry extra vendor config (environment, scac, etc.)
            // as JSON; merge it so operators can extend without code changes.
            var config = new Dictionary<string, object>
            {
                ["apiKey"]     = p.ApiKey ?? "",
                ["billToCode"] = "",                 // not stored as a column; may live in Instructions
            };

            if (!string.IsNullOrWhiteSpace(p.EndpointUrl))
                config["baseUrlOverride"] = p.EndpointUrl;

            string configJson;
            if (!string.IsNullOrWhiteSpace(p.Instructions))
            {
                // Instructions is authoritative for any keys it sets; start from it, then
                // fill apiKey/baseUrl if Instructions didn't provide them.
                try
                {
                    var fromInstructions = JsonConvert.DeserializeObject<Dictionary<string, object>>(p.Instructions)
                                           ?? new Dictionary<string, object>();
                    foreach (var kv in config)
                        if (!fromInstructions.ContainsKey(kv.Key))
                            fromInstructions[kv.Key] = kv.Value;
                    configJson = JsonConvert.SerializeObject(fromInstructions);
                }
                catch
                {
                    // Instructions wasn't valid JSON — fall back to the column-built config.
                    configJson = JsonConvert.SerializeObject(config);
                }
            }
            else
            {
                configJson = JsonConvert.SerializeObject(config);
            }

            return new ClientProfile
            {
                ProfileId     = p.Id,
                ShipperCode   = p.CustomerID.ToString(),
                VendorName    = p.AdapterName,
                IsActive      = p.Active,
                EnabledEvents = "LocationReportedEvent,LoadStatusEvent",
                ConfigJson    = configJson
            };
        }
    }
}
