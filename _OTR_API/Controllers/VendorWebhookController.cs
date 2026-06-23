using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Vendor.Common.Dispatch;
using Vendor.Common.Persistence;
using Vendor.Common.Security;

namespace OTR_API.Controllers
{
    /// <summary>
    /// Generic inbound webhook receiver. ONE endpoint serves every vendor; the vendor is
    /// identified by the {vendor} route segment and resolved by name through the
    /// VendorAdapterRegistry. Adding a vendor never touches this controller — a dev drops
    /// a Vendor.{Name} DLL, registers it in &lt;vendorAdapters&gt;, and the route works.
    ///
    /// REQUEST PIPELINE (in order):
    ///   1. Resolve the vendor's validator + processor from the registry. Unknown -> 404.
    ///   2. IP allowlist gate (network layer). Not allowed -> 403.
    ///   3. Auth gate (apikey/basic/hmac/none) via the vendor's validator. Fail -> 401.
    ///   4. Parse correlation metadata (inline, fast, never throws).
    ///   5. Dedupe-persist the raw callback (hash + upsert). Returns 200 fast.
    /// Heavy correlation happens later on the background WebhookCorrelator thread.
    ///
    /// RESPONSE CODES follow webhook conventions:
    ///   200 — accepted (including duplicates; idempotent)
    ///   401 — auth failed   (vendor may retry after fixing creds; we don't persist)
    ///   403 — IP not allowed (we don't persist)
    ///   404 — unknown vendor (we don't persist)
    ///   503 — transient server/db failure (vendor should retry)
    /// </summary>
    [RoutePrefix("api/vendorwebhook")]
    public class VendorWebhookController : ApiController
    {
        // http://localhost:5129/api/vendorwebhook/{vendor}
        [HttpPost, Route("{vendor}")]
        public async Task<HttpResponseMessage> Receive(string vendor)
        {
            // Read the raw body once — needed for HMAC validation and for persistence.
            string rawBody;
            try
            {
                rawBody = Request.Content != null
                    ? await Request.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : string.Empty;
            }
            catch (Exception ex)
            {
                Audit(ex.ToString(), "VendorWebhook.ReadBody");
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }

            // Guard: framework must be configured (Application_Start wires this).
            if (!VendorDispatcher.IsConfigured)
            {
                Audit("VendorDispatcher not configured.", "VendorWebhook");
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }

            var registry = VendorDispatcher.Instance.Registry;

            // ── 1. Resolve vendor's validator + processor ──────────────────
            var validator = registry.GetValidator(vendor);
            var processor = registry.GetInboundProcessor(vendor);
            if (validator == null || processor == null)
            {
                // Unknown vendor or vendor not configured for inbound. Don't persist.
                Audit($"No inbound handlers registered for vendor '{vendor}'.", "VendorWebhook");
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }

            // ── 2. IP allowlist gate ───────────────────────────────────────
            var remoteIp = GetRemoteIp();
            var allowedIps = registry.GetAllowedSourceIps(vendor);   // null/empty => allow all
            if (!IpAllowlist.IsAllowed(remoteIp, allowedIps))
            {
                Audit($"Vendor '{vendor}': source IP '{remoteIp}' not in allowlist.", "VendorWebhook");
                return Request.CreateResponse(HttpStatusCode.Forbidden);
            }

            // ── 3. Auth gate ───────────────────────────────────────────────
            var headers = CollectHeaders();
            if (!validator.IsValid(headers, rawBody))
            {
                Audit($"Vendor '{vendor}': webhook authentication failed.", "VendorWebhook");
                return Request.CreateResponse(HttpStatusCode.Unauthorized);
            }

            // ── 4. Parse correlation metadata (inline, never throws) ───────
            var metadata = processor.ParseAndExtract(rawBody);

            // ── 5. Dedupe-persist ──────────────────────────────────────────
            try
            {
                var connStr = VendorDispatcher.Instance.AuditConnectionString;
                var repo = new InboundCallbackRepository(connStr);
                var payloadHash = Sha256Hex(rawBody);

                await repo.UpsertAsync(vendor, payloadHash, rawBody, metadata, CancellationToken.None)
                          .ConfigureAwait(false);

                // Accepted. Correlation happens later on the background thread.
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                // Persistence failure is critical — return 503 so the vendor retries.
                Audit(ex.ToString(), "VendorWebhook.Persist");
                return Request.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }
        }

        // ─── Helpers ───────────────────────────────────────────────────────

        /// <summary>Best-effort client IP from the current HTTP context.</summary>
        private static string GetRemoteIp()
        {
            try
            {
                var ctx = HttpContext.Current;
                if (ctx?.Request != null)
                {
                    // Honor a forwarded header if a trusted proxy sets it; otherwise UserHostAddress.
                    var fwd = ctx.Request.Headers["X-Forwarded-For"];
                    if (!string.IsNullOrWhiteSpace(fwd))
                    {
                        // X-Forwarded-For may be a comma list; the first entry is the origin client.
                        var first = fwd.Split(',')[0].Trim();
                        if (!string.IsNullOrEmpty(first)) return first;
                    }
                    return ctx.Request.UserHostAddress;
                }
            }
            catch { /* fall through */ }
            return null;
        }

        /// <summary>Flattens request headers into a case-insensitive dictionary (first value per key).</summary>
        private Dictionary<string, string> CollectHeaders()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in Request.Headers)
            {
                if (h.Value == null) continue;
                using (var e = h.Value.GetEnumerator())
                {
                    if (e.MoveNext()) dict[h.Key] = e.Current;
                }
            }
            // Content headers (e.g., Authorization sometimes lands here depending on host) —
            // include them too without overwriting request-level headers.
            if (Request.Content?.Headers != null)
            {
                foreach (var h in Request.Content.Headers)
                {
                    if (h.Value == null) continue;
                    if (dict.ContainsKey(h.Key)) continue;
                    using (var e = h.Value.GetEnumerator())
                    {
                        if (e.MoveNext()) dict[h.Key] = e.Current;
                    }
                }
            }
            return dict;
        }

        private static string Sha256Hex(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void Audit(string message, string source)
        {
            try
            {
                new OTR_API.DataClasses.DataAudit().InsertErrorAuditLog(message, source);
            }
            catch { /* never let logging break the response */ }
        }
    }
}
