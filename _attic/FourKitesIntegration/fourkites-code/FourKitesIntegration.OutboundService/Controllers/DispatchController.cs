using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using FourKitesIntegration.Core.Client;
using FourKitesIntegration.Core.Models.CreateShipment;
using FourKitesIntegration.Core.Models.DispatcherUpdate;
using FourKitesIntegration.Core.Models.Documents;
using FourKitesIntegration.Core.Persistence;

namespace FourKitesIntegration.OutboundService.Controllers
{
    /// <summary>
    /// HTTP API exposed to Vector FBS for sending FourKites updates.
    /// Vector posts JSON payloads here; this controller serializes and forwards to FourKites,
    /// logging both directions in FourKitesOutboundTransactions.
    /// </summary>
    [RoutePrefix("api/fourkites")]
    public class DispatchController : ApiController
    {
        // Singleton FourKitesClient — constructed once per service lifetime.
        private static readonly FourKitesClient _client = BuildClient();
        private static readonly OutboundTransactionRepository _txRepo = BuildRepo();

        private static FourKitesClient BuildClient()
        {
            var opts = new FourKitesClientOptions
            {
                Environment = ConfigurationManager.AppSettings["FourKites.Environment"] ?? "Staging",
                BaseHost = ConfigurationManager.AppSettings["FourKites.BaseHost"] ?? "api-staging.fourkites.com",
                ApiKey = ConfigurationManager.AppSettings["FourKites.ApiKey"],
                MaxRetryAttempts = int.TryParse(ConfigurationManager.AppSettings["FourKites.MaxRetryAttempts"], out var n) ? n : 3
            };
            return new FourKitesClient(opts);
        }

        private static OutboundTransactionRepository BuildRepo()
        {
            var cs = ConfigurationManager.AppSettings["ConnectionString"];
            return new OutboundTransactionRepository(cs);
        }

        // ─── Endpoints ───────────────────────────────────────────────────────

        /// <summary>
        /// POST /api/fourkites/create-shipment
        /// Body: a CreateShipmentRequest. Returns the FourKitesResponse to Vector.
        /// </summary>
        [HttpPost, Route("create-shipment")]
        public async Task<IHttpActionResult> CreateShipment([FromBody] CreateShipmentEnvelope envelope)
        {
            if (envelope?.Request?.Load == null) return BadRequest("Missing load object.");

            var tx = new OutboundTransaction
            {
                VectorLoadId = envelope.VectorLoadId,
                UpdateType = "createShipment",
                BillToCode = null, // Create Shipment doesn't use BillToCode
                PrimaryReference = envelope.Request.Load.LoadNumber,
                ExpectedCallbackType = "LOAD_CREATION",
                RequestPayload = FourKitesJson.Serialize(envelope.Request)
            };
            var txId = await _txRepo.InsertPendingAsync(tx).ConfigureAwait(false);

            var response = await _client.CreateShipmentAsync(envelope.Request).ConfigureAwait(false);
            var newStatus = response.IsSuccess ? TransactionStatus.Ack : TransactionStatus.HttpFail;
            await _txRepo.RecordResponseAsync(txId, response.StatusCode, response.RequestId, response.Body, newStatus)
                .ConfigureAwait(false);

            return Content(MapToHttpStatus(response.StatusCode), response, Configuration.Formatters.JsonFormatter);
        }

        /// <summary>
        /// POST /api/fourkites/dispatch-update
        /// Body: a DispatcherBatch. Returns the FourKitesResponse.
        /// </summary>
        [HttpPost, Route("dispatch-update")]
        public async Task<IHttpActionResult> DispatchUpdate([FromBody] DispatcherBatchEnvelope envelope)
        {
            if (envelope?.Batch?.Updates == null || envelope.Batch.Updates.Count == 0)
                return BadRequest("Missing updates.");

            // For multi-load batches, we log a single row representing the batch.
            var firstEntry = envelope.Batch.Updates[0];
            var firstIdentifier = firstEntry.IdentifierKeys.Count > 0 ? firstEntry.IdentifierKeys[0].Identifier : null;
            var updateType = InferUpdateType(firstEntry);

            var tx = new OutboundTransaction
            {
                VectorLoadId = envelope.VectorLoadId,
                UpdateType = updateType,
                BillToCode = firstEntry.BillToCode,
                PrimaryReference = firstIdentifier,
                ExpectedCallbackType = InferExpectedCallback(updateType),
                RequestPayload = FourKitesJson.Serialize(envelope.Batch)
            };
            var txId = await _txRepo.InsertPendingAsync(tx).ConfigureAwait(false);

            var response = await _client.SendDispatcherUpdateAsync(envelope.Batch).ConfigureAwait(false);
            var newStatus = response.IsSuccess ? TransactionStatus.Ack : TransactionStatus.HttpFail;
            await _txRepo.RecordResponseAsync(txId, response.StatusCode, response.RequestId, response.Body, newStatus)
                .ConfigureAwait(false);

            return Content(MapToHttpStatus(response.StatusCode), response, Configuration.Formatters.JsonFormatter);
        }

        /// <summary>
        /// POST /api/fourkites/upload-document
        /// Body: an UploadDocumentRequest. Returns the FourKitesResponse.
        /// </summary>
        [HttpPost, Route("upload-document")]
        public async Task<IHttpActionResult> UploadDocument([FromBody] UploadDocumentEnvelope envelope)
        {
            if (envelope?.Request?.Documents == null || envelope.Request.Documents.Count == 0)
                return BadRequest("No documents in request.");

            var tx = new OutboundTransaction
            {
                VectorLoadId = envelope.VectorLoadId,
                UpdateType = "uploadDocument",
                PrimaryReference = envelope.Request.Load?.Value,
                ExpectedCallbackType = "NONE", // No publicly-indexed document webhook; verify with GetDocument
                RequestPayload = "(base64 content truncated)" // Don't log full 10 MB of base64
            };
            var txId = await _txRepo.InsertPendingAsync(tx).ConfigureAwait(false);

            var response = await _client.UploadDocumentAsync(envelope.Request).ConfigureAwait(false);
            var newStatus = response.IsSuccess ? TransactionStatus.Ack : TransactionStatus.HttpFail;
            await _txRepo.RecordResponseAsync(txId, response.StatusCode, response.RequestId, response.Body, newStatus)
                .ConfigureAwait(false);

            return Content(MapToHttpStatus(response.StatusCode), response, Configuration.Formatters.JsonFormatter);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static HttpStatusCode MapToHttpStatus(int code)
        {
            if (code == 0) return HttpStatusCode.BadGateway;
            return (HttpStatusCode)code;
        }

        private static string InferUpdateType(LoadUpdateEntry entry)
        {
            if (entry.LoadUpdate == null || entry.LoadUpdate.Count == 0) return "unknown";
            var first = entry.LoadUpdate[0];
            if (first.LocationUpdate != null) return "locationUpdate";
            if (first.EventUpdate != null) return "eventUpdate";
            if (first.StopUpdate != null) return "stopUpdate";
            if (first.AssignmentUpdate != null) return "assignmentUpdate";
            if (first.LoadInfoUpdate != null) return "loadInfoUpdate";
            if (first.EtaUpdate != null) return "etaUpdate";
            if (first.TemperatureUpdate != null) return "temperatureUpdate";
            return "unknown";
        }

        private static string InferExpectedCallback(string updateType)
        {
            // See reference doc Section 7.7 for the loop-closure mapping.
            switch (updateType)
            {
                case "stopUpdate":      return "STOP_APPOINTMENT_RESCHEDULED";
                case "loadInfoUpdate":  return "LOAD_UPDATE";
                case "eventUpdate":     return "STOP_ARRIVAL|STOP_DEPARTURE|STOP_AUTO_DELIVERED";
                case "etaUpdate":       return "CARRIER_ETA_UPDATED";
                default:                return "NONE";
            }
        }
    }

    /// <summary>Envelope wrapping a Create Shipment payload with Vector-side metadata.</summary>
    public class CreateShipmentEnvelope
    {
        public string VectorLoadId { get; set; }
        public CreateShipmentRequest Request { get; set; }
    }

    public class DispatcherBatchEnvelope
    {
        public string VectorLoadId { get; set; }
        public DispatcherBatch Batch { get; set; }
    }

    public class UploadDocumentEnvelope
    {
        public string VectorLoadId { get; set; }
        public UploadDocumentRequest Request { get; set; }
    }
}
