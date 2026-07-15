using OTR_API.TruckerToolsTracking.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using OTR_API.TruckerTools.DataClasses;
using OTR_API.Filters;
using System.Threading.Tasks;

namespace OTR_API.Controllers
{
    [HMACAuthentication]
    [RoutePrefix("api/truckertools")]
    public class TruckerToolsTrackingController : ApiController
    {

        //http://localhost:5129/api/truckertoolstracking/TrackLoad
        [HttpPost]
        public TrackingResponse TrackLoad([FromBody]Load load)
        {
            TrackingResponse response = new TrackingResponse();
            response.response = new Response();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (load == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "LoadTrack");
                response.response.Message = "Error Tracking Load";
            }
            else {
                

                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();

                try
                {
                    load = dtt.AddLoadTrackingTimeZones(load);
                    int LoadID = dtt.InsertLoadTracking(load);

                    try
                    {
                        WebCallFunctions wc = new WebCallFunctions();

                        Task<TrackingResponse> task1 = Task.Run(() => { return wc.PostTrackLoad(load); });

                        response = task1.Result;

                        response.response.TrackingID = LoadID;

                        dtt.InsertLoadResponse(response);

                        // ─── Vendor check call: load assignment ───────────────
                        // FIRE-AND-FORGET. Fully isolated from TT response.
                        // FK failures are audited but NEVER surface to FBS.
                        Task.Run(() =>
                        {
                            try
                            {
                                Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                                    BuildLoadAssignedEvent(load));
                            }
                            catch (Exception vdEx)
                            {
                                try { da.InsertErrorAuditLog(vdEx.Message, "TrackLoad.VendorDispatch"); } catch { }
                            }
                        });
                        // ──────────────────────────────────────────────────────
                    }
                    catch (Exception ex)
                    {
                        response.response.Message = "Error Posting Load - " + ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "TrackLoad");
                    response.response.Message = "Error Saving Load";
                }
            }
            return response;
        }

        //http://localhost:5129/api/truckertoolstracking/UpdateTrackLoad
        [HttpPost]
        public TrackingResponse UpdateTrackLoad([FromBody]Load load)
        {
            TrackingResponse response = new TrackingResponse();
            response.response = new Response();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (load == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "LoadTrack");
                response.response.Message = "Error Tracking Load";
            }
            else {

                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();

                try
                {
                    load = dtt.AddLoadTrackingTimeZones(load);
                    int LoadID = dtt.UpdateLoadTracking(load);

                    try
                    {
                        WebCallFunctions wc = new WebCallFunctions();

                        Task<TrackingResponse> task1 = Task.Run(() => { return wc.PutUpdateTrackLoad(load); });

                        response = task1.Result;

                        response.response.TrackingID = LoadID;

                        dtt.InsertLoadResponse(response);

                        // ─── Vendor check call: load assignment (update) ──────────
                        // FIRE-AND-FORGET. Fully isolated from TT response.
                        Task.Run(() =>
                        {
                            try
                            {
                                Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                                    BuildLoadAssignedEvent(load));
                            }
                            catch (Exception vdEx)
                            {
                                try { da.InsertErrorAuditLog(vdEx.Message, "UpdateTrackLoad.VendorDispatch"); } catch { }
                            }
                        });
                        // ──────────────────────────────────────────────────────
                    }
                    catch (Exception ex)
                    {
                        response.response.Message = "Error Posting Load - " + ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "TrackLoad");
                    response.response.Message = "Error Saving Load";
                }
            }
            return response;
        }

        //http://localhost:5129/api/truckertoolstracking/CancelLoadTracking
        [HttpPost]
        public TrackingResponse CancelLoadTracking([FromBody]Load load)
        {
            TrackingResponse response = new TrackingResponse();
            response.response = new Response();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (load == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "CancelLoadTracking");
                response.response.Message = "Error Cancelling Load Tracking";
            }
            else {

                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();

                try
                {
                    int TrackingID = dtt.InsertTrackingCancellation(load);

                    try
                    {
                        WebCallFunctions wc = new WebCallFunctions();

                        Task<TrackingResponse> task1 = Task.Run(() => { return wc.CancelLoadTracking(load); });

                        response = task1.Result;

                        response.response.TrackingID = TrackingID;

                        dtt.InsertLoadResponse(response);

                        // ─── Vendor dispatch: tracking stopped ────────────────
                        // FIRE-AND-FORGET. Fully isolated from TT response.
                        Task.Run(() =>
                        {
                            try
                            {
                                Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                                    new Vendor.Common.Events.LoadTrackingStoppedEvent
                                    {
                                        VectorLoadId = load.VectorID.ToString(),
                                        ShipmentNumber = (load.shipper != null && load.shipper.loadNumber != null) ? load.shipper.loadNumber : "",
                                        BillToID = load.BillToID,
                                        SourceSystem = "OTR_API",
                                        Reason = "CANCELLED"
                                    });
                            }
                            catch (Exception vdEx)
                            {
                                try { da.InsertErrorAuditLog(vdEx.Message, "CancelLoadTracking.VendorDispatch"); } catch { }
                            }
                        });
                        // ──────────────────────────────────────────────────────
                    }
                    catch (Exception ex)
                    {
                        response.response.Message = "Error Cancelling Load - " + ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "CancelLoadTracking");

                    response.response = new Response();
                    response.response.Message = "Error Cancelling Load Tracking";
                }
            }
            return response;
        }


        // http://localhost:5129/api/truckertoolstracking/GetTrackedLoad?VectorID=12345668
        [HttpGet]
        public Load GetTrackedLoad(int VectorID)
        {
            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            if (VectorID <= 0)
            {
                da.InsertErrorAuditLog("VectorID is required", "GetTrackedLoad");
                return null;
            }

            try
            {
                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                return dtt.GetTrackedLoad(VectorID);
            }
            catch (Exception ex)
            {
                da.InsertErrorAuditLog(ex.Message, "GetTrackedLoad");
                return null;
            }
        }

        // http://localhost:5129/api/truckertoolstracking/PostPOD
        // Live POD endpoint. FBS reads a file off disk, converts to bytes, base64-encodes,
        // and POSTs this JSON body. OTR decodes and dispatches a DocumentAvailableEvent
        // through the VVIProfiles-routed vendor dispatcher (same customer-scoping as
        // SendStatus). Fire-and-forget — the response returns as soon as the event is
        // queued; check VendorOutboundTransactions for the FK dispatch outcome.
        //
        // Expected JSON body:
        //   {
        //     "VectorID":     901731,
        //     "FileName":     "pod-901731.pdf",
        //     "MimeType":     "application/pdf",
        //     "DocumentType": "ProofOfDelivery",   // optional; default POD
        //     "Content":      "<base64-encoded bytes>"
        //   }
        [HttpPost]
        public StatusResponse PostPOD([FromBody]PodUploadRequest req)
        {
            StatusResponse response = new StatusResponse();
            response.timeStamp = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss tt K");

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            if (req == null)
            {
                da.InsertErrorAuditLog("Body is null", "PostPOD");
                response.status = false;
                response.errorCode = 300;
                response.errorMessage = "Request body is required.";
                return response;
            }

            if (req.VectorID <= 0)
            {
                da.InsertErrorAuditLog("VectorID is required", "PostPOD");
                response.status = false;
                response.errorCode = 301;
                response.errorMessage = "VectorID is required.";
                return response;
            }

            if (string.IsNullOrWhiteSpace(req.Content))
            {
                da.InsertErrorAuditLog("Content is empty for VectorID=" + req.VectorID, "PostPOD");
                response.status = false;
                response.errorCode = 304;
                response.errorMessage = "Content (base64) is required.";
                return response;
            }

            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(req.Content);
            }
            catch (FormatException fx)
            {
                da.InsertErrorAuditLog("Invalid base64 Content: " + fx.Message, "PostPOD");
                response.status = false;
                response.errorCode = 305;
                response.errorMessage = "Content is not valid base64.";
                return response;
            }

            if (fileBytes.Length == 0)
            {
                da.InsertErrorAuditLog("Decoded content is 0 bytes for VectorID=" + req.VectorID, "PostPOD");
                response.status = false;
                response.errorCode = 306;
                response.errorMessage = "Decoded content is empty.";
                return response;
            }

            try
            {
                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                Load lookupLoad = dtt.GetTrackedLoad(req.VectorID);

                if (lookupLoad == null || lookupLoad.VectorID <= 0)
                {
                    da.InsertErrorAuditLog("No tracked load found for VectorID=" + req.VectorID, "PostPOD");
                    response.status = false;
                    response.errorCode = 302;
                    response.errorMessage = "No tracked load found.";
                    return response;
                }

                if (lookupLoad.BillToID <= 0)
                {
                    da.InsertErrorAuditLog("BillToID=0 for VectorID=" + req.VectorID + " — cannot dispatch POD without customer scope", "PostPOD.NoCustomerScope");
                    response.status = false;
                    response.errorCode = 303;
                    response.errorMessage = "BillToID missing on Tracking row.";
                    return response;
                }

                string shipmentNumber = (lookupLoad.shipper != null && lookupLoad.shipper.loadNumber != null) ? lookupLoad.shipper.loadNumber : "";
                string fileName = !string.IsNullOrWhiteSpace(req.FileName) ? req.FileName : ("pod-" + lookupLoad.VectorID + ".pdf");
                string mimeType = !string.IsNullOrWhiteSpace(req.MimeType) ? req.MimeType : "application/pdf";

                // Parse DocumentType with a POD default.
                Vendor.Common.Events.DocumentType docType = Vendor.Common.Events.DocumentType.ProofOfDelivery;
                if (!string.IsNullOrWhiteSpace(req.DocumentType))
                {
                    Vendor.Common.Events.DocumentType parsed;
                    if (Enum.TryParse(req.DocumentType, ignoreCase: true, result: out parsed))
                        docType = parsed;
                }

                // Fire-and-forget so an FK failure never blocks the OTR response.
                Task.Run(() =>
                {
                    try
                    {
                        Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                            new Vendor.Common.Events.DocumentAvailableEvent
                            {
                                VectorLoadId = lookupLoad.VectorID.ToString(),
                                ShipmentNumber = shipmentNumber,
                                BillToID = lookupLoad.BillToID,
                                SourceSystem = "OTR_API",
                                DocumentType = docType,
                                FileName = fileName,
                                MimeType = mimeType,
                                Content = fileBytes,
                                CapturedUtc = DateTime.UtcNow
                            });
                    }
                    catch (Exception vdEx)
                    {
                        try { da.InsertErrorAuditLog(vdEx.Message, "PostPOD.VendorDispatch"); } catch { }
                    }
                });

                response.status = true;
                response.Message = "POD dispatched for VectorID=" + req.VectorID + ", ShipmentNumber=" + shipmentNumber + " (" + fileBytes.Length + " bytes)";
                return response;
            }
            catch (Exception ex)
            {
                da.InsertErrorAuditLog(ex.Message, "PostPOD");
                response.status = false;
                response.errorCode = 200;
                response.errorMessage = ex.Message;
                return response;
            }
        }

        /// <summary>
        /// Request body shape for POST /api/truckertoolstracking/PostPOD. FBS/POD app
        /// reads a file off disk, converts to a byte array, base64-encodes it, and sends
        /// as JSON. OTR decodes and dispatches a DocumentAvailableEvent through the
        /// vendor dispatcher.
        /// </summary>
        public class PodUploadRequest
        {
            /// <summary>Vector FBS LoadID. Required. Used to look up the Tracking row for BillToID + ShipmentID.</summary>
            public int VectorID { get; set; }
            /// <summary>Original filename for vendor display. Optional; defaults to "pod-{VectorID}.pdf".</summary>
            public string FileName { get; set; }
            /// <summary>MIME type. Optional; defaults to "application/pdf".</summary>
            public string MimeType { get; set; }
            /// <summary>DocumentType enum name. Optional; defaults to ProofOfDelivery.</summary>
            public string DocumentType { get; set; }
            /// <summary>Base64-encoded file bytes. Required.</summary>
            public string Content { get; set; }
        }

        /// <summary>
        /// Returns a valid minimal PDF (single blank page) as a byte array. Handy for
        /// wiring/testing the POD upload path without needing a real file on disk.
        /// The bytes below are the smallest well-formed PDF that Adobe Reader will open.
        /// </summary>
        private byte[] BuildMinimalPdf()
        {
            string pdf =
                "%PDF-1.1\n" +
                "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
                "2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj\n" +
                "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>endobj\n" +
                "xref\n0 4\n" +
                "0000000000 65535 f \n" +
                "0000000009 00000 n \n" +
                "0000000052 00000 n \n" +
                "0000000098 00000 n \n" +
                "trailer<</Size 4/Root 1 0 R>>\nstartxref\n149\n%%EOF";
            return System.Text.Encoding.ASCII.GetBytes(pdf);
        }

        //http://localhost:5129/api/truckertoolstracking/GetAvailableLoads
        [HttpGet]
        public TrackingResponse GetLoadsTracked()
        {
            TrackingResponse response = new TrackingResponse();

            DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();
            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            try
            {
                WebCallFunctions wc = new WebCallFunctions();

                Task<TrackingResponse> task1 = Task.Run(() => { return wc.GetLoadsTracked(); });

                response = task1.Result;

            }
            catch (Exception ex)
            {
                da.InsertErrorAuditLog(ex.Message, "GetLoadsTracked");
                //response.Message = "Error Getting Available Loads - " + ex.Message;
            }


            return response;
        }


        //http://localhost:5129/api/truckertoolstracking/sendstatus
        [HttpPost]
        public StatusResponse SendStatus([FromBody]StatusUpdate lc)
        {
            StatusResponse response = new StatusResponse();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
            if (lc == null)
            {
                da.InsertErrorAuditLog("Body Message is null", "StatusResponse");
                response.Message = "Error Adding Status Response";
            }
            else
            {
                WebCallFunctions wc = new WebCallFunctions();

                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                DateTime dte = DateTime.Now;


                // Status - Type : 1 = LatestStatus, 2 = Status 
                // Location - Type : 1 = LatestStatus, 2 = Status Location, 3 = LatestLocation, 4 = Location List 


                try
                {
                    response.timeStamp = dte.ToString("MM/dd/yyyy HH:mm:ss tt K");

                    try
                    {
                        int result = 0;
                        int StatusID = 0;

                        if (!wc.VerifyTrackingAccount(lc))
                            result = -1;

                        if(result > -1)
                        {
                            //Insert Status
                            StatusID = dtt.InsertLoadTrackingStatus(lc);
                            lc.ID = StatusID;
                            response.StatusID = StatusID;
                            response.loadID = lc.loadNumber;


                            if (StatusID > 0)
                            {
                                if (lc.latestStatus != null)
                                {
                                    //Insert LatestStatus
                                    lc.latestStatus.associatedId = lc.ID;
                                    lc.latestStatus.type = 1;

                                    int result2 = dtt.InsertLoadTrackingStatusInfo(lc.latestStatus);
                                    lc.latestStatus.ID = result2;

                                    if (lc.latestStatus.location != null)
                                    {

                                        if (!dtt.ValidateGeoCoordinate(lc.latestStatus.location.lat))
                                        {
                                            result = -4;
                                        }

                                        if (!dtt.ValidateGeoCoordinate(lc.latestStatus.location.lon))
                                        {
                                            result = -5;
                                        }

                                        lc.latestStatus.location.associatedId = lc.latestStatus.ID;
                                        lc.latestStatus.location.type = 1;

                                        dtt.InsertLoadTrackingStatusLocation(lc.latestStatus.location);

                                    }
                                }


                                if (lc.status != null)
                                {
                                    //Insert Status
                                    lc.status.associatedId = lc.ID;
                                    lc.status.type = 2;
                                    int result3 = dtt.InsertLoadTrackingStatusInfo(lc.status);
                                    lc.status.ID = result3;

                                    if (lc.status.location != null)
                                    {
                                        if (!dtt.ValidateGeoCoordinate(lc.status.location.lat))
                                        {
                                            result = -4;
                                        }

                                        if (!dtt.ValidateGeoCoordinate(lc.status.location.lon))
                                        {
                                            result = -5;
                                        }

                                        lc.status.location.associatedId = lc.status.ID;
                                        lc.status.location.type = 2;

                                        dtt.InsertLoadTrackingStatusLocation(lc.status.location);

                                    }
                                }

                                if (lc.latestLocation != null)
                                {
                                    if(lc.latestLocation.lat != null)
                                    {
                                        if (lc.latestLocation.lon != null)
                                        {
                                            if (!dtt.ValidateGeoCoordinate(lc.latestLocation.lat))
                                            {
                                                result = -4;
                                            }

                                            if (!dtt.ValidateGeoCoordinate(lc.latestLocation.lon))
                                            {
                                                result = -5;
                                            }

                                            //Insert LatestLocation
                                            lc.latestLocation.associatedId = lc.ID;
                                            lc.latestLocation.type = 3;

                                            dtt.InsertLoadTrackingStatusLocation(lc.latestLocation);
                                        }
                                    }


                                }


                                if (lc.locations != null)
                                {
                                    foreach (Location loc in lc.locations)
                                    {
                                        loc.type = 4;
                                        loc.associatedId = StatusID;
                                        //Insert multi-locations
                                        dtt.InsertLoadTrackingStatusLocation(loc);
                                    }
                                }
                            }
                            else
                                result = -2;

                        }
                        switch (result)
                        {
                            case -6:
                                response.errorCode = 304;
                                response.errorMessage = "The Device Timestamp is Required.";
                                response.status = false;
                                break;
                            case -5:
                                response.errorCode = 303;
                                response.errorMessage = "The Longitude is Required.";
                                response.status = false;
                                break;
                            case -4:
                                response.errorCode = 302;
                                response.errorMessage = "The Latitude is Required.";
                                response.status = false;
                                break;
                            case -3:
                                response.errorCode = 301;
                                response.errorMessage = "The External Load ID is Required.";
                                response.status = false;
                                break;
                            case -2:
                                response.errorCode = 200;
                                response.errorMessage = "An Internal System Error Occurred.";
                                response.status = false;
                                break;
                            case -1:

                                response.errorCode = 100;
                                response.errorMessage = "Invalid Account.";
                                response.status = false;
                                break;
                            default:
                                response.status = true;
                                break;
                        }

                    }
                    catch (Exception ex)
                    {
                        response.status = false;
                        response.errorCode = 200;
                        response.Message = ex.Message;
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatus");
                    }

                    // ─── Vendor dispatch: status + location ─────────────────
                    // Fires only on successful inserts. Resolves VectorLoadId via
                    // dtt.GetLoadTracking. Branches on per-vendor verbosity (DB-driven):
                    //   Generous     : one event per data point in the TT payload
                    //   Conservative : freshest location + freshest status
                    // Adapter rate limiters are the second line of defense regardless.
                    // Failures here never break the OTR API operation.
                    try
                    {
                        if (response.status)
                        {
                            Load lookupLoad = new Load();
                            lookupLoad.loadNumber = lc.loadNumber;
                            lookupLoad = dtt.GetLoadTracking(lookupLoad);

                            // Customer scope guard: BillToID identifies which customer's VVI
                            // profile should receive this dispatch. 0 means the tracking record
                            // predates the FBS BillToID plumbing, or FBS didn't populate it.
                            // Skip dispatch rather than risk sending status to the wrong customer's
                            // vendor account (cross-customer leak). See else-if below for audit trail.
                            if (lookupLoad != null && lookupLoad.VectorID > 0 && lookupLoad.BillToID > 0)
                            {
                                string vectorLoadId = lookupLoad.VectorID.ToString();
                                int billToId = lookupLoad.BillToID;
                                string shipmentNumber = (lookupLoad.shipper != null && lookupLoad.shipper.loadNumber != null) ? lookupLoad.shipper.loadNumber : "";
                                string verbosity = Vendor.Common.Dispatch.VendorDispatcher.Instance
                                    .GetDispatchVerbosity("FourKites");
                                bool generous = string.Equals(verbosity, "Generous",
                                    StringComparison.OrdinalIgnoreCase);

                                // Location events
                                DispatchLocation(vectorLoadId, shipmentNumber, billToId, lc.latestLocation, da);
                                if (generous)
                                {
                                    if (lc.latestStatus != null)
                                        DispatchLocation(vectorLoadId, shipmentNumber, billToId, lc.latestStatus.location, da);
                                    if (lc.status != null)
                                        DispatchLocation(vectorLoadId, shipmentNumber, billToId, lc.status.location, da);
                                    if (lc.locations != null)
                                    {
                                        foreach (Location loc in lc.locations)
                                            DispatchLocation(vectorLoadId, shipmentNumber, billToId, loc, da);
                                    }
                                }
                                else if (lc.latestLocation == null)
                                {
                                    // Conservative + no latestLocation: fall back to embedded location
                                    Location fallback = null;
                                    if (lc.latestStatus != null && lc.latestStatus.location != null)
                                        fallback = lc.latestStatus.location;
                                    else if (lc.status != null && lc.status.location != null)
                                        fallback = lc.status.location;
                                    if (fallback != null)
                                        DispatchLocation(vectorLoadId, shipmentNumber, billToId, fallback, da);
                                }

                                // Status events
                                if (generous)
                                {
                                    DispatchStatus(vectorLoadId, shipmentNumber, billToId, lc.latestStatus, da);
                                    DispatchStatus(vectorLoadId, shipmentNumber, billToId, lc.status, da);
                                }
                                else
                                {
                                    DispatchStatus(vectorLoadId, shipmentNumber, billToId, lc.latestStatus ?? lc.status, da);
                                }
                            }
                            else if (lookupLoad != null && lookupLoad.VectorID > 0 && lookupLoad.BillToID <= 0)
                            {
                                // Have a VectorLoadId but no customer scope. Log for visibility
                                // and skip. Once FBS is deployed with BillToID plumbing, this row
                                // firing on new records means FBS is not populating BillToID.
                                da.InsertErrorAuditLog(
                                    "SKIPPED SendStatus vendor dispatch: BillToID=0 for VectorLoadId=" + lookupLoad.VectorID + ", loadNumber=" + lc.loadNumber,
                                    "SendStatus.VendorDispatch.NoCustomerScope");
                            }
                        }
                    }
                    catch (Exception vdEx)
                    {
                        da.InsertErrorAuditLog(vdEx.Message, "SendStatus.VendorDispatch");
                    }
                    // ────────────────────────────────────────────────────

                    try
                    {
                        int responseID = dtt.InsertLoadTrackingStatusResponse(response);

                        //try
                        //{
                        //    Load ld = new Load();
                        //    ld.loadNumber = lc.loadNumber;
                        //    ld = dtt.GetLoadTracking(ld);

                        //    OTR_API.DataClasses.Communicate cc = new OTR_API.DataClasses.Communicate();
                        //    string Body = "A Status Update has been Completed";

                        //    var emailresponse = cc.SendEmail(ld.dispatcherEmail, Body, "LoadID " + lc.loadNumber + " - Load Status");
                        //}
                        //catch (Exception ex)
                        //{
                        //    da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse | Send Email");
                        //}
                    }
                    catch(Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse");
                    }


                    if(!response.status)
                    {

                    }

                    ////Success{
                    ////    "status”: true,
                    ////    "timeStamp":"06/24/2013 09:02:44 AM -0600"
                    ////}
                    ////Failure{
                    ////    "status”: false,
                    ////    "errorCode":100,
                    ////    "errorMessage":"Invalid Account"
                    ////}

                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "StatusResponse");
                    response.Message = "Error Saving Status Response";
                }
            }

            return response;
        }



        //http://localhost:5129/api/truckertoolstracking/gettrackingstatusbyID
        [HttpPost]
        public StatusUpdate GetTrackingStatusByID([FromBody]StatusUpdate lc)
        {
            StatusUpdate response = new StatusUpdate();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            if (lc == null)
            {
                da.InsertErrorAuditLog("Tracking Status is null", "StatusUpdate");
                response.Message = "Error Pulling Tracking Status";
            }
            else
            {
                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                DateTime dte = DateTime.Now;

                try
                {

                    response = dtt.GetLoadTrackingStatus(lc);

                }
                catch (Exception ex)
                {
                    response.Message = ex.Message;
                    da.InsertErrorAuditLog(ex.Message, "GetTrackingStatus");
                }

            }

            return response;
        }

        //http://localhost:5129/api/truckertoolstracking/gettrackingstatusloadID
        [HttpPost]
        public List<StatusUpdate> GetTrackingStatusLoadID([FromBody]Load load)
        {
            List<StatusUpdate> response = new List<StatusUpdate>();

            OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();

            if (load == null)
            {
                da.InsertErrorAuditLog("Tracking Status is null", "GetTrackingStatusLoadID");
            }
            else
            {
                DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
                DateTime dte = DateTime.Now;

                try
                {

                    response = dtt.GetLoadTrackingStatusList(load);


                }
                catch (Exception ex)
                {
                    da.InsertErrorAuditLog(ex.Message, "GetTrackingStatusLoadID");
                }

            }

            return response;
        }

        // ─── Vendor dispatch helpers (used by SendStatus) ────────────────────────────────

        /// <summary>
        /// Builds a fully-populated LoadAssignedEvent from a tracking Load. Used by both
        /// TrackLoad and UpdateTrackLoad insertion points so the payload they send is
        /// identical in shape. Pulls every field the tracking model has available; null
        /// fields stay null in the event (and are dropped by the FK adapter where
        /// appropriate).
        /// </summary>
        private Vendor.Common.Events.LoadAssignedEvent BuildLoadAssignedEvent(Load load)
        {
            return new Vendor.Common.Events.LoadAssignedEvent
            {
                VectorLoadId   = load.VectorID.ToString(),
                ShipmentNumber = (load.shipper != null && load.shipper.loadNumber != null) ? load.shipper.loadNumber : "",
                BillToID       = load.BillToID,
                SourceSystem   = "OTR_API",
                ExternalLoadId = load.loadTrackExternalId,
                LoadType       = load.loadType,
                TrailerType    = load.trailerType,
                LoadNotes      = load.loadNotes,
                IsTeamLoad     = load.isTeamLoad,

                Carrier = load.carrier == null ? null : new Vendor.Common.Events.CarrierInfo
                {
                    Name      = load.carrier.companyName,
                    McNumber  = load.carrier.docketNumber
                    // scac/dotNumber not on the tracking Carrier model
                },

                Driver = new Vendor.Common.Events.DriverInfo
                {
                    Name       = load.driverName,
                    Phone      = load.driverCell,
                    DriverType = load.driverType,
                    Comments   = load.driverComments
                    // email not on tracking model
                },

                Equipment = new Vendor.Common.Events.EquipmentInfo
                {
                    TruckNumber   = load.truckNumber,
                    TrailerNumber = load.trailerNumber,
                    TrailerType   = load.trailerType
                    // vin/licensePlate not on tracking model
                },

                Dispatcher = new Vendor.Common.Events.DispatcherInfo
                {
                    Id    = load.dispatcherId,
                    Email = load.dispatcherEmail,
                    Phone = load.dispatcherPhoneNumber
                    // dispatcher Name not on tracking model
                },

                Shipper = load.shipper == null ? null : new Vendor.Common.Events.ShipperInfo
                {
                    ShipperId          = load.shipper.shipperId,
                    ReferenceNumber    = load.shipper.referenceNumber,
                    NotificationEmails = load.shipper.emails
                },

                Stops = BuildStopInfoList(load.stops)
            };
        }

        /// <summary>
        /// Converts a tracking Stop list into the framework's StopInfo list. Assigns
        /// pickup/delivery/intermediate roles by position (first = pickup, last = delivery,
        /// middle = intermediate). Datetimes are parsed best-effort; unparseable strings
        /// yield null UTC times (vendor still sees the raw values via notes).
        /// </summary>
        private List<Vendor.Common.Events.StopInfo> BuildStopInfoList(List<Stop> stops)
        {
            if (stops == null || stops.Count == 0) return null;

            var result = new List<Vendor.Common.Events.StopInfo>(stops.Count);
            int last = stops.Count - 1;

            for (int i = 0; i < stops.Count; i++)
            {
                var s = stops[i];
                if (s == null) continue;

                Vendor.Common.Events.StopRole role;
                if (i == 0) role = Vendor.Common.Events.StopRole.Pickup;
                else if (i == last) role = Vendor.Common.Events.StopRole.Delivery;
                else role = Vendor.Common.Events.StopRole.Intermediate;

                result.Add(new Vendor.Common.Events.StopInfo
                {
                    SequenceNumber       = i + 1,
                    Role                 = role,
                    AddressLine1         = s.address,
                    City                 = s.city,
                    State                = s.state,
                    PostalCode           = s.zipcode,
                    Latitude             = s.lat.ToString(),
                    Longitude            = s.lon.ToString(),
                    Notes                = s.notes,
                    ExternalStopId       = s.stopExternalId,
                    ScheduledArrivalUtc  = TryParseStopUtc(s.datetime),
                    ScheduledDepartureUtc= TryParseStopUtc(s.datetimeExit)
                });
            }

            return result;
        }

        /// <summary>
        /// Stop times in the tracking model may carry a trailing TZ abbreviation
        /// (e.g., "2026-01-15 08:00:00 CST") that DateTime.TryParse may or may not
        /// handle depending on locale. Try the raw string; on failure, try stripping
        /// trailing tokens; give up and return null. Caller treats null as "unscheduled".
        /// </summary>
        private DateTime? TryParseStopUtc(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            DateTime parsed;
            if (DateTime.TryParse(s, out parsed))
                return parsed.ToUniversalTime();

            // Strip trailing word (likely TZ abbreviation) and retry.
            int lastSpace = s.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string trimmed = s.Substring(0, lastSpace).Trim();
                if (DateTime.TryParse(trimmed, out parsed))
                    return parsed.ToUniversalTime();
            }

            return null;
        }

        /// <summary>
        /// Dispatches a LocationReportedEvent if the given TT location has valid coords.
        /// No-ops on null or empty lat/lon. Errors are logged to DataAudit, never thrown.
        /// </summary>
        private void DispatchLocation(string vectorLoadId, string shipmentNumber, int billToId, Location loc, OTR_API.DataClasses.DataAudit da)
        {
            if (loc == null) return;
            if (string.IsNullOrEmpty(loc.lat) || string.IsNullOrEmpty(loc.lon)) return;

            try
            {
                Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                    new Vendor.Common.Events.LocationReportedEvent
                    {
                        VectorLoadId = vectorLoadId,
                        ShipmentNumber = shipmentNumber,
                        BillToID = billToId,
                        SourceSystem = "OTR_API",
                        Latitude = loc.lat,
                        Longitude = loc.lon,
                        City = loc.city,
                        State = loc.state,
                        Country = loc.country,
                        LocatedAtUtc = TryParseUtc(loc.timeStamp)
                    });
            }
            catch (Exception ex)
            {
                da.InsertErrorAuditLog(ex.Message, "SendStatus.VendorDispatch.Location");
            }
        }

        /// <summary>
        /// Dispatches a LoadStatusEvent if the given TT status has a recognizable code.
        /// Translates TT code to LoadStatusType via TruckToolsStatusMapper. Preserves
        /// raw code in SourceStatusCode so adapters can pass it through verbatim.
        /// No-ops on null or empty code/name. Errors are logged to DataAudit, never thrown.
        /// </summary>
        private void DispatchStatus(string vectorLoadId, string shipmentNumber, int billToId, Status status, OTR_API.DataClasses.DataAudit da)
        {
            if (status == null) return;
            string rawCode = !string.IsNullOrEmpty(status.code) ? status.code : status.name;
            if (string.IsNullOrEmpty(rawCode)) return;

            try
            {
                // Ask the mapper how to handle this TT signal. It knows the whitelist,
                // AC parsing rules, and when to auto-fire an "En Route" (AG) follow-up.
                var decision = OTR_API.DataClasses.TruckToolsStatusMapper.Resolve(
                    status.code, status.name);

                DateTime statusTimeUtc = TryParseUtc(status.timeStamp);

                // Primary event — fires for PE / PX / DE / DX and for AC variants we
                // recognize. SX skips this and goes straight to the follow-up.
                if (decision.DispatchPrimary)
                {
                    Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                        new Vendor.Common.Events.LoadStatusEvent
                        {
                            VectorLoadId = vectorLoadId,
                            ShipmentNumber = shipmentNumber,
                            BillToID = billToId,
                            SourceSystem = "OTR_API",
                            StatusType = decision.PrimaryType,
                            StatusTimeUtc = statusTimeUtc,
                            SourceStatusCode = status.code,
                            SourceStatusDescription = status.name
                        });
                }

                // Auto-follow "En Route" (AG) — fires after departures so vendors see the
                // truck advance without waiting for a separate in-transit signal.
                if (decision.FollowWithInTransit)
                {
                    Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                        new Vendor.Common.Events.LoadStatusEvent
                        {
                            VectorLoadId = vectorLoadId,
                            ShipmentNumber = shipmentNumber,
                            BillToID = billToId,
                            SourceSystem = "OTR_API",
                            StatusType = Vendor.Common.Events.LoadStatusType.InTransit,
                            StatusTimeUtc = statusTimeUtc,
                            SourceStatusCode = "AG",
                            SourceStatusDescription = "En Route"
                        });
                }
            }
            catch (Exception ex)
            {
                da.InsertErrorAuditLog(ex.Message, "SendStatus.VendorDispatch.Status");
            }
        }

        /// <summary>
        /// Parses a TT timestamp string to DateTime UTC. Falls back to DateTime.UtcNow
        /// if the string is empty or unparseable so the event still carries a sensible
        /// timestamp.
        /// </summary>
        private DateTime TryParseUtc(string s)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(s) && DateTime.TryParse(s, out parsed))
                return parsed.ToUniversalTime();
            return DateTime.UtcNow;
        }
    }
}