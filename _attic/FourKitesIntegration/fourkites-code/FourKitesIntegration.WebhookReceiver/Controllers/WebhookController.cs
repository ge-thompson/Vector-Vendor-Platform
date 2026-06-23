using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using FourKitesIntegration.Core.Client;
using Newtonsoft.Json.Linq;

namespace FourKitesIntegration.WebhookReceiver.Controllers
{
    /// <summary>
    /// POST /fourkites/webhook — receives FourKites callbacks.
    /// CRITICAL design rules:
    ///   • Return 200 quickly. Heavy processing must NOT happen inline.
    ///   • Return 5xx on transient failures (FourKites will retry: 1m, 15m, 1h, 24h).
    ///   • Return 4xx ONLY for unrecoverable issues — 4xx kills delivery permanently.
    ///   • Dedupe — FourKites may retry the same callback.
    /// </summary>
    [RoutePrefix("fourkites")]
    public class WebhookController : ApiController
    {
        private static readonly InboundCallbacksRepository _repo =
            new InboundCallbacksRepository(ConfigurationManager.AppSettings["ConnectionString"]);

        /// <summary>Health check — no auth required.</summary>
        [HttpGet, Route("~/health")]
        public IHttpActionResult Health() =>
            Json(new { status = "ok", service = "FourKitesWebhookReceiver" });

        [HttpPost, Route("webhook")]
        public async Task<HttpResponseMessage> ReceiveWebhook()
        {
            string rawBody;
            try
            {
                rawBody = await Request.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Transport-layer failure reading the body — return 5xx, let FourKites retry.
                LogError("Failed to read webhook body: " + ex);
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }

            if (string.IsNullOrEmpty(rawBody))
            {
                LogWarning("Empty webhook body received.");
                // Empty body is unrecoverable — won't help to retry. Return 200 to acknowledge.
                return Request.CreateResponse(HttpStatusCode.OK);
            }

            // Parse minimal envelope fields. Anything beyond that, the background processor handles.
            string messageType = null;
            long? fkLoadId = null;
            string loadNumber = null;
            string referenceNumbersJson = null;

            try
            {
                var obj = JObject.Parse(rawBody);
                messageType = obj["MessageType"]?.Value<string>();
                fkLoadId = obj["FourKitesLoadId"]?.Value<long?>();
                loadNumber = obj["LoadNumber"]?.Value<string>();
                referenceNumbersJson = obj["ReferenceNumbers"]?.ToString();
            }
            catch (Exception ex)
            {
                // JSON malformed. Logging the raw payload helps debugging. Returning 5xx makes FourKites retry,
                // which is the conservative choice — but if the payload is genuinely malformed every retry will fail.
                LogError("Failed to parse webhook JSON: " + ex);
                LogPayloadToFile(rawBody, "malformed");
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                bool isNew = await _repo.InsertIfNewAsync(messageType, fkLoadId, loadNumber, referenceNumbersJson, rawBody)
                    .ConfigureAwait(false);
                if (!isNew)
                    LogInfo($"Duplicate webhook ignored: {messageType} {fkLoadId}");

                // Return 200 fast; the background processor (not included in this MVP) does the matching/correlation
                // by querying InboundCallbacks for unprocessed rows. This keeps the receiver simple and resilient.
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                // Database failure — return 5xx for retry.
                LogError("Failed to persist webhook: " + ex);
                LogPayloadToFile(rawBody, "db-failure");
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }
        }

        // ─── Logging helpers ─────────────────────────────────────────────────
        // Replace with your team's standard logger (Serilog, NLog, etc.) once decided.

        private static void LogInfo(string msg) =>
            System.Diagnostics.EventLog.WriteEntry("FourKitesWebhookReceiver", msg,
                System.Diagnostics.EventLogEntryType.Information);

        private static void LogWarning(string msg) =>
            System.Diagnostics.EventLog.WriteEntry("FourKitesWebhookReceiver", msg,
                System.Diagnostics.EventLogEntryType.Warning);

        private static void LogError(string msg) =>
            System.Diagnostics.EventLog.WriteEntry("FourKitesWebhookReceiver", msg,
                System.Diagnostics.EventLogEntryType.Error);

        private static void LogPayloadToFile(string body, string tag)
        {
            try
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "failed-payloads");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, $"{tag}_{DateTime.UtcNow:yyyyMMddTHHmmssfff}.json");
                File.WriteAllText(file, body);
            }
            catch { /* best-effort */ }
        }
    }
}
