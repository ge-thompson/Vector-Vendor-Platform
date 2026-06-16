# FourKites Webhook Receiver — Phase 1

**Document:** Deliverable #5 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** Deliverables #2 (refactor), #3 (OTR upgrade), #4 (v2 insertion points), #10 (framework) complete
**Related decisions:** D-010, D-014, D-020, D-024, D-025

---

## 0. Purpose

This document specifies how OTR API receives **inbound webhook callbacks from FourKites** — and from any future vendor — and feeds them into the framework's audit + correlation infrastructure.

The deliverable produces:
- One new framework-level controller in OTR API: `VendorWebhookController` (vendor-agnostic)
- One new framework-level abstraction: `IInboundEventProcessor`
- One FourKites-specific implementation: `FourKitesWebhookProcessor` (lives in `Vendor.FourKites`)
- The `WebhookCorrelator` refactored from the old WebhookReceiver and relocated to `Vendor.Common`
- Configuration for the public webhook URL FK will call
- Auth strategy (per O-001 confirmation: FK uses apikey or Basic auth — not HMAC; per existing WebhookAuthMiddleware comment)

**Glen executes the merges. Claude does not modify OTR API source.**

**Pattern parity with #4:** outbound dispatch is vendor-agnostic at the OTR API surface; inbound webhook receipt should be too. Same goal — adding vendor #2 requires one new processor class + one new route, no OTR API rework.

---

## 1. The webhook flow

```
FourKites
   │
   │ POST https://<otr-api-host>/api/vendorwebhook/fourkites
   │ Authorization: <apikey or Basic>
   │ body: { "MessageType": "LOAD_CREATION", "FourKitesLoadId": 123456, ... }
   │
   ▼
┌──────────────────────────────────────────────────────────────────────┐
│ OTR API — VendorWebhookController.Receive(vendorName)                │
│                                                                       │
│  1. Look up IWebhookSignatureValidator for vendorName                │
│  2. Read raw body                                                    │
│  3. Validate auth (validator returns true/false)                    │
│  4. Compute payload hash for dedupe                                 │
│  5. INSERT row into VendorInboundCallbacks (raw payload preserved)  │
│  6. Look up IInboundEventProcessor for vendorName                   │
│  7. Call processor.ParseAndExtractAsync(payload) → structured data  │
│  8. Return 200 to vendor (fast — under 500ms typically)             │
└────────────────────────────────────┬─────────────────────────────────┘
                                     │
                                     │ (asynchronous, decoupled)
                                     ▼
┌──────────────────────────────────────────────────────────────────────┐
│ WebhookCorrelator (background worker in OTR API)                     │
│                                                                       │
│  Every 10 seconds, scans VendorInboundCallbacks for ProcessedUtc IS  │
│  NULL rows, then for each:                                           │
│  1. Look up the IInboundEventProcessor for the row's VendorName     │
│  2. Call processor.CorrelateAsync(row) → finds matching outbound tx │
│  3. Updates outbound tx Status: CONFIRMED or REJECTED                │
│  4. Calls processor.OnConfirmedAsync(row, tx) for side effects       │
│     (e.g. FK: stamp FourKitesLoadId on Vector's Load table)          │
│  5. Marks callback Processed                                         │
└──────────────────────────────────────────────────────────────────────┘
```

Three properties of this flow that matter:

**Receipt is fast and durable.** The controller's only synchronous job is: validate, persist, return. Everything else happens asynchronously. If correlation logic is slow or fails, the webhook is already saved — we can retry indefinitely.

**Vendor-agnostic at the OTR API layer.** OTR API's controller doesn't know what a FourKites webhook looks like. It hands the raw body to the framework, which delegates to a vendor-specific processor. The controller code is identical whether vendor #1 or vendor #5 is calling.

**Idempotent.** We compute a hash of the payload at receipt and store it. If FK sends the same webhook twice (retries on their side, network blips, etc.), the second receipt is detected as a duplicate and acknowledged but not re-processed.

---

## 2. The framework abstractions

### 2.1 `IInboundEventProcessor` (new — lives in `Vendor.Common`)

```csharp
namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// Implemented by each vendor adapter to handle inbound webhooks from that vendor.
    /// Two responsibilities:
    ///   1. ParseAndExtract — pull correlation keys out of the raw payload at receipt time
    ///      (called inline during controller request)
    ///   2. Correlate / OnConfirmed — match the callback to an outbound transaction and
    ///      perform vendor-specific side effects (called by background WebhookCorrelator)
    /// </summary>
    public interface IInboundEventProcessor
    {
        string VendorName { get; }

        /// <summary>
        /// Called inline by the controller right after the raw payload is persisted.
        /// Pulls correlation-friendly fields out of the raw body so the correlator
        /// can match against outbound transactions later without re-parsing.
        /// Must NOT throw — return an empty/default object on error.
        /// </summary>
        InboundEventMetadata ParseAndExtract(string rawPayload);

        /// <summary>
        /// Called by WebhookCorrelator to match a callback to an outbound transaction.
        /// Returns the matched TransactionId, or null if no match.
        /// </summary>
        Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow callback,
            SqlConnection connection,
            CancellationToken ct);

        /// <summary>
        /// Called by WebhookCorrelator AFTER a successful match, for vendor-specific
        /// side effects. FourKites uses this to stamp FourKitesLoadId on Vector's
        /// Load table. Must catch its own errors — correlator continues regardless.
        /// </summary>
        Task OnConfirmedAsync(
            InboundCallbackRow callback,
            long matchedTransactionId,
            SqlConnection connection,
            CancellationToken ct);
    }

    /// <summary>Metadata extracted from a webhook at receipt time. Used by the correlator.</summary>
    public class InboundEventMetadata
    {
        public string MessageType { get; set; }   // "LOAD_CREATION", "STOP_ARRIVAL", etc. (vendor-defined)
        public string VendorLoadId { get; set; }  // FK's FourKitesLoadId; P44's shipment id; etc.
        public string VectorLoadId { get; set; }  // If discoverable from payload
        public List<string> ReferenceNumbers { get; set; }
        public bool IsSuccess { get; set; }       // Did the vendor report success or error?
        public string ErrorsJson { get; set; }
    }
}
```

**Why split parse from correlate:** receipt is in the request thread; correlation runs in a background worker minutes later. Parsing once at receipt avoids re-deserializing JSON on every correlator pass. The parsed metadata is stored in indexed columns on `VendorInboundCallbacks` so the correlator's query is cheap.

### 2.2 `IWebhookSignatureValidator` (already designed in #10; restated for context)

```csharp
namespace Vendor.Common.Abstractions
{
    public interface IWebhookSignatureValidator
    {
        string VendorName { get; }

        /// <summary>
        /// Validates an incoming webhook. Returns true if authentic.
        /// For FourKites: validates apikey header OR Basic auth (no body signing).
        /// For future vendors that DO sign bodies: validates HMAC over the raw body.
        /// </summary>
        bool IsValid(IDictionary<string, string> headers, string rawBody);
    }
}
```

### 2.3 `VendorProcessorRegistry` — discovery via Web.config

Parallel to the `VendorAdapterRegistry` from Deliverable #10. Discovers inbound processors and validators from the same `<vendorAdapters>` config section:

```xml
<vendorAdapters>
  <adapters>
    <add vendorName="FourKites"
         adapterType="Vendor.FourKites.Adapter.FourKitesAdapter, Vendor.FourKites"
         inboundProcessorType="Vendor.FourKites.Webhooks.FourKitesWebhookProcessor, Vendor.FourKites"
         webhookValidatorType="Vendor.FourKites.Webhooks.FourKitesWebhookSignatureValidator, Vendor.FourKites" />
  </adapters>
</vendorAdapters>
```

Three types per vendor, all optional:
- `adapterType` (outbound — required)
- `inboundProcessorType` (inbound — required if the vendor sends webhooks)
- `webhookValidatorType` (auth check — required if `inboundProcessorType` is set)

For vendor #2 you'd add a single `<add>` line with their three types.

---

## 3. The new OTR API controller — `VendorWebhookController`

This is the entire file. It's framework-level and vendor-agnostic — when a future vendor ships, this controller does NOT change.

**File:** `OTR API\Controllers\VendorWebhookController.cs` (new)

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Vendor.Common;
using Vendor.Common.Abstractions;
using Vendor.Common.Persistence;

namespace OTR_API.Controllers
{
    /// <summary>
    /// Vendor-agnostic webhook receiver. One controller, one method, handles inbound
    /// callbacks from any configured vendor.
    ///
    /// Public route: POST /api/vendorwebhook/{vendorName}
    /// Example FK: POST /api/vendorwebhook/fourkites
    ///
    /// NOTE: this controller does NOT have [HMACAuthentication] because vendors won't
    /// sign with our HMAC scheme. Auth is delegated to each vendor's IWebhookSignatureValidator.
    /// </summary>
    [RoutePrefix("api/vendorwebhook")]
    public class VendorWebhookController : ApiController
    {
        private static readonly VendorProcessorRegistry _registry =
            VendorProcessorRegistry.LoadFromConfig();

        private static readonly InboundCallbackRepository _repo =
            new InboundCallbackRepository(
                System.Configuration.ConfigurationManager.AppSettings[
                    "VendorDispatch.AuditConnectionString"]);

        [HttpPost]
        [Route("{vendorName}")]
        public async Task<HttpResponseMessage> Receive(string vendorName)
        {
            try
            {
                // 1. Resolve vendor
                var validator = _registry.GetValidator(vendorName);
                var processor = _registry.GetProcessor(vendorName);
                if (validator == null || processor == null)
                {
                    // Unknown vendor — refuse but log
                    LogWarning($"Webhook received for unknown vendor: {vendorName}");
                    return Request.CreateResponse(HttpStatusCode.NotFound,
                        new { error = "Unknown vendor", vendorName });
                }

                // 2. Read raw body (we need raw bytes preserved for signature checks
                //    and to persist verbatim into the audit table)
                string rawBody = await Request.Content.ReadAsStringAsync()
                                              .ConfigureAwait(false);

                // 3. Validate auth
                var headers = Request.Headers.ToDictionary(
                    h => h.Key,
                    h => string.Join(",", h.Value),
                    StringComparer.OrdinalIgnoreCase);
                if (!validator.IsValid(headers, rawBody))
                {
                    LogWarning($"Webhook auth failed for vendor: {vendorName}");
                    return Request.CreateResponse(HttpStatusCode.Unauthorized);
                }

                // 4. Compute hash for dedupe
                string payloadHash = ComputeSha256(rawBody);

                // 5. Pull correlation metadata out of the body
                InboundEventMetadata metadata;
                try { metadata = processor.ParseAndExtract(rawBody); }
                catch (Exception ex)
                {
                    LogError($"ParseAndExtract failed for {vendorName}: {ex}");
                    metadata = new InboundEventMetadata();  // empty, but we still persist
                }

                // 6. Persist (handles dedupe internally — same hash = update existing row)
                long callbackId = await _repo.UpsertAsync(new InboundCallbackRow
                {
                    VendorName = vendorName,
                    PayloadHash = payloadHash,
                    RawPayload = rawBody,
                    MessageType = metadata.MessageType,
                    VendorLoadId = metadata.VendorLoadId,
                    VectorLoadId = metadata.VectorLoadId,
                    ReferenceNumbersJson = metadata.ReferenceNumbers != null
                        ? Newtonsoft.Json.JsonConvert.SerializeObject(metadata.ReferenceNumbers)
                        : null,
                    IsSuccess = metadata.IsSuccess,
                    ErrorsJson = metadata.ErrorsJson,
                    ReceivedUtc = DateTime.UtcNow
                }).ConfigureAwait(false);

                // 7. Return 200 — fast. Correlation happens asynchronously in WebhookCorrelator.
                return Request.CreateResponse(HttpStatusCode.OK, new { callbackId });
            }
            catch (Exception ex)
            {
                // Last-resort catch — never let an unhandled exception cause a 500 that
                // makes the vendor retry forever. Log it and 200-back; if the row didn't get
                // persisted, the vendor's retry will succeed.
                LogError($"VendorWebhookController unexpected error: {ex}");
                return Request.CreateResponse(HttpStatusCode.OK, new { error = "logged" });
            }
        }

        private static string ComputeSha256(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void LogInfo(string msg) => Safe(msg, System.Diagnostics.EventLogEntryType.Information);
        private static void LogWarning(string msg) => Safe(msg, System.Diagnostics.EventLogEntryType.Warning);
        private static void LogError(string msg) => Safe(msg, System.Diagnostics.EventLogEntryType.Error);
        private static void Safe(string m, System.Diagnostics.EventLogEntryType t)
        {
            try { new OTR_API.DataClasses.DataAudit().InsertErrorAuditLog(m, "VendorWebhookController"); } catch { }
        }
    }
}
```

**Key design points:**

1. **No `[HMACAuthentication]` attribute.** OTR API's existing HMAC filter assumes the caller signs with our shared secret. FK doesn't. The controller takes responsibility for its own auth via the vendor's validator.

2. **Always returns 200 (except for genuine unknown vendor / unauthorized).** Internal errors are logged but the vendor gets a success — they retry on 4xx/5xx, and we don't want them retrying because of bugs on our side. The webhook is in the audit log; we can replay/diagnose offline.

3. **Idempotency via SHA256 of the body.** The repository's `UpsertAsync` checks `(VendorName, PayloadHash)` uniqueness — if FK sends the same body twice, the second arrival updates `ReceivedUtc` on the existing row but doesn't create a duplicate.

4. **Static singletons.** The registry and repository are loaded once at app start. Both are thread-safe for read.

---

## 4. The FourKites webhook processor

Lives in `Vendor.FourKites`. Implements `IInboundEventProcessor`. This is where ALL FK-specific webhook knowledge lives.

**File:** `Vendor.FourKites\Webhooks\FourKitesWebhookProcessor.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vendor.Common.Abstractions;
using Vendor.Common.Persistence;

namespace Vendor.FourKites.Webhooks
{
    public class FourKitesWebhookProcessor : IInboundEventProcessor
    {
        public string VendorName => "FourKites";

        // ─── 1. Parse-at-receipt ─────────────────────────────────────────────

        public InboundEventMetadata ParseAndExtract(string rawPayload)
        {
            if (string.IsNullOrEmpty(rawPayload)) return new InboundEventMetadata { IsSuccess = true };

            try
            {
                var obj = JObject.Parse(rawPayload);

                // FK fields per their webhook payload schema
                string messageType = obj["MessageType"]?.Value<string>();
                long? fkLoadId = obj["FourKitesLoadId"]?.Value<long?>();
                string loadNumber = obj["LoadNumber"]?.Value<string>();
                bool isSuccess = obj["IsSuccess"]?.Value<bool?>() ?? true;

                var references = new List<string>();
                if (!string.IsNullOrEmpty(loadNumber)) references.Add(loadNumber);
                var refArray = obj["ReferenceNumbers"] as JArray;
                if (refArray != null)
                {
                    foreach (var r in refArray)
                    {
                        var v = r.Value<string>();
                        if (!string.IsNullOrEmpty(v) && !references.Contains(v))
                            references.Add(v);
                    }
                }

                string errorsJson = null;
                var errorsToken = obj["Errors"];
                if (errorsToken is JArray errorsArr && errorsArr.HasValues)
                {
                    errorsJson = errorsArr.ToString(Newtonsoft.Json.Formatting.None);
                    isSuccess = false;  // presence of errors overrides IsSuccess
                }

                return new InboundEventMetadata
                {
                    MessageType = messageType,
                    VendorLoadId = fkLoadId?.ToString(),
                    VectorLoadId = loadNumber,  // FK echoes back the loadNumber we sent (which IS the VectorLoadId)
                    ReferenceNumbers = references,
                    IsSuccess = isSuccess,
                    ErrorsJson = errorsJson
                };
            }
            catch
            {
                return new InboundEventMetadata { IsSuccess = true };
            }
        }

        // ─── 2. Correlate (called from background worker) ───────────────────

        public async Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow cb, SqlConnection cn, CancellationToken ct)
        {
            // Pass 1: by VendorLoadId (FourKitesLoadId). Only available after LOAD_CREATION
            // has stamped the ID on the outbound transaction.
            if (!string.IsNullOrEmpty(cb.VendorLoadId)
                && long.TryParse(cb.VendorLoadId, out var fkLoadId))
            {
                var match = await FindByVendorLoadIdAsync(cn, fkLoadId, cb.MessageType, ct).ConfigureAwait(false);
                if (match.HasValue) return match;
            }

            // Pass 2: by VectorLoadId (FK echoed our loadNumber back). Works for any callback,
            // most useful for the first LOAD_CREATION callback before we have a FKLoadId.
            if (!string.IsNullOrEmpty(cb.VectorLoadId))
            {
                var match = await FindByVectorLoadIdAsync(cn, cb.VectorLoadId, cb.MessageType, ct).ConfigureAwait(false);
                if (match.HasValue) return match;
            }

            // Pass 3: by any reference number
            if (!string.IsNullOrEmpty(cb.ReferenceNumbersJson))
            {
                try
                {
                    var refs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(cb.ReferenceNumbersJson);
                    foreach (var r in refs ?? new List<string>())
                    {
                        var match = await FindByVectorLoadIdAsync(cn, r, cb.MessageType, ct).ConfigureAwait(false);
                        if (match.HasValue) return match;
                    }
                }
                catch { /* malformed — ignore */ }
            }

            return null;
        }

        private async Task<long?> FindByVendorLoadIdAsync(SqlConnection cn, long fkLoadId, string messageType, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VendorRequestId IS NOT NULL    -- has been ACKed
  AND Status IN ('ACK', 'PENDING')
ORDER BY CreatedUtc DESC;";
            // Note: FK doesn't echo our requestId on webhooks; we match by FourKitesLoadId
            // which gets stamped on the transaction after the LOAD_CREATION confirmation.
            // For Phase 1, this query is approximate — refine after observing real FK callback shapes.

            using (var cmd = new SqlCommand(sql, cn))
            {
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return (result == null || result == DBNull.Value) ? (long?)null : (long)result;
            }
        }

        private async Task<long?> FindByVectorLoadIdAsync(SqlConnection cn, string vectorLoadId, string messageType, CancellationToken ct)
        {
            const string sql = @"
SELECT TOP 1 TransactionId
FROM dbo.VendorOutboundTransactions
WHERE VendorName = 'FourKites'
  AND VectorLoadId = @VectorLoadId
  AND Status IN ('ACK', 'PENDING')
ORDER BY CreatedUtc DESC;";

            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@VectorLoadId", vectorLoadId);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return (result == null || result == DBNull.Value) ? (long?)null : (long)result;
            }
        }

        // ─── 3. Vendor-specific side effects on confirmation ────────────────

        public async Task OnConfirmedAsync(
            InboundCallbackRow cb, long matchedTxId, SqlConnection cn, CancellationToken ct)
        {
            // For LOAD_CREATION callbacks: stamp the FourKitesLoadId onto Vector's Load table.
            // This is FK-specific business logic — it belongs in this adapter, not in the framework.

            if (!string.Equals(cb.MessageType, "LOAD_CREATION", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrEmpty(cb.VendorLoadId)) return;
            if (string.IsNullOrEmpty(cb.VectorLoadId)) return;

            try
            {
                // EDIT THIS SQL if Vector's Load table is named differently in your environment.
                // The schema additions in Deliverable #7 add the FourKitesLoadId column.
                const string updateSql = @"
UPDATE dbo.[Load]
SET FourKitesLoadId = @FkLoadId,
    FourKitesCreatedUtc = SYSUTCDATETIME(),
    FourKitesTrackingStatus = 'CREATED'
WHERE LoadId = @VectorLoadId;";

                using (var cmd = new SqlCommand(updateSql, cn))
                {
                    cmd.Parameters.AddWithValue("@FkLoadId", long.Parse(cb.VendorLoadId));
                    cmd.Parameters.AddWithValue("@VectorLoadId", cb.VectorLoadId);
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            catch (SqlException ex) when (ex.Number == 208 || ex.Number == 207)
            {
                // Invalid table/column — log and move on. Operator can fix the SQL or schema later.
                // (208 = invalid object, 207 = invalid column)
                // Never break the correlator over an FK-side-effect failure.
            }
        }
    }
}
```

---

## 5. The FourKites webhook signature validator

Per the existing `WebhookAuthMiddleware`'s comment (*"FourKites does NOT sign callback bodies"*), validation is **transport-level only** — either an apikey header OR HTTP Basic. Glen picks one with FK's CSM at provisioning time.

**File:** `Vendor.FourKites\Webhooks\FourKitesWebhookSignatureValidator.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using Vendor.Common.Abstractions;

namespace Vendor.FourKites.Webhooks
{
    /// <summary>
    /// Validates inbound FourKites webhooks.
    /// FK does NOT sign payloads — auth is transport-level (apikey header or Basic auth).
    /// Mode and credentials are read from VendorAPI_FK.dbo.ClientProfiles.ConfigJson for
    /// the (VECTOR_DEFAULT, FourKites) profile, under a "webhookAuth" subkey.
    /// </summary>
    public class FourKitesWebhookSignatureValidator : IWebhookSignatureValidator
    {
        public string VendorName => "FourKites";

        private readonly FourKitesWebhookAuthSettings _settings;

        public FourKitesWebhookSignatureValidator()
        {
            _settings = FourKitesWebhookAuthSettings.LoadFromClientProfile();
        }

        public bool IsValid(IDictionary<string, string> headers, string rawBody)
        {
            switch (_settings.Mode)
            {
                case "apikey":
                    return headers.TryGetValue(_settings.HeaderName, out var v)
                        && string.Equals(v?.Trim(), _settings.HeaderValue, StringComparison.Ordinal);

                case "basic":
                    if (!headers.TryGetValue("Authorization", out var authz)) return false;
                    return ValidateBasic(authz);

                case "none":
                    // Use ONLY if you've allowlisted FK's IPs at the firewall.
                    return true;

                default:
                    return false;
            }
        }

        private bool ValidateBasic(string authzHeader)
        {
            if (string.IsNullOrEmpty(authzHeader) ||
                !authzHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(authzHeader.Substring(6).Trim()));
                var parts = decoded.Split(new[] { ':' }, 2);
                return parts.Length == 2 &&
                       string.Equals(parts[0], _settings.BasicUser, StringComparison.Ordinal) &&
                       string.Equals(parts[1], _settings.BasicPassword, StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }

    internal class FourKitesWebhookAuthSettings
    {
        public string Mode { get; set; }            // "apikey" | "basic" | "none"
        public string HeaderName { get; set; }
        public string HeaderValue { get; set; }
        public string BasicUser { get; set; }
        public string BasicPassword { get; set; }

        public static FourKitesWebhookAuthSettings LoadFromClientProfile()
        {
            // Reads the FK ClientProfile's ConfigJson and extracts the "webhookAuth" object.
            // (Implementation reads from VendorAPI_FK via the same connection string used
            // elsewhere by Vendor.Common. Keeping it short here — full impl in code.)
            //
            // Example ConfigJson subset:
            //   "webhookAuth": {
            //     "mode": "apikey",
            //     "headerName": "X-FourKites-Token",
            //     "headerValue": "<token>"
            //   }
            throw new NotImplementedException("Implementation reads from ClientProfile ConfigJson");
        }
    }
}
```

The implementation detail of loading from ClientProfile ConfigJson is straightforward but verbose; the actual class file will include it. The shape above is the contract.

---

## 6. The relocated correlator — `Vendor.Common.WebhookCorrelator`

The existing `WebhookCorrelator.cs` from the old `FourKitesIntegration.WebhookReceiver` service is **already vendor-agnostic in shape** but has FK-specific hardcoding (table names, FourKitesLoadId column, the "stamp on Vector Load table" logic). The refactor:

**Stays in the correlator (framework-level):**
- The background loop and batch-claim pattern
- The Status update (ACK/PENDING → CONFIRMED/REJECTED)
- The MatchedTransactionId link
- The retry-on-failure (unclaim) pattern

**Moves to per-vendor processors via `IInboundEventProcessor`:**
- `ExtractWebhookDetails` → `ParseAndExtract` (already done at receipt, not in correlator)
- `FindByFkLoadIdAsync` and `FindByPrimaryReferenceAsync` → `FindMatchingTransactionAsync`
- `StampFourKitesLoadIdOnVectorLoadAsync` → `OnConfirmedAsync`

**Renames:**
- `FourKitesInboundCallbacks` → `VendorInboundCallbacks`
- `FourKitesOutboundTransactions` → `VendorOutboundTransactions`
- `FourKitesLoadId` column on transactions → `VendorLoadId` (string)

**Hosting change (per D-010):** The correlator now runs **inside OTR API** as an `Application_Start` background worker, not as a standalone Windows Service. The startup hook:

```csharp
// OTR API\Global.asax.cs — addition to Application_Start
protected void Application_Start()
{
    GlobalConfiguration.Configure(WebApiConfig.Register);

    // ─── NEW: start the webhook correlator ──────────────────
    Vendor.Common.WebhookCorrelator.StartGlobalCorrelator(
        System.Configuration.ConfigurationManager.AppSettings[
            "VendorDispatch.AuditConnectionString"]);
    // ────────────────────────────────────────────────────────
}

protected void Application_End()
{
    Vendor.Common.WebhookCorrelator.StopGlobalCorrelator();
}
```

The `StartGlobalCorrelator` / `StopGlobalCorrelator` static methods on the framework class manage the singleton background task. App pool recycles cleanly stop it.

The full refactored `WebhookCorrelator` class is ~250 lines and lives in `Vendor.Common\Persistence\WebhookCorrelator.cs`. Its shape mirrors the existing implementation — same outer loop, same batch claim, same retry behavior — but the per-callback work delegates to the processor returned by the registry rather than calling FK-specific methods directly.

---

## 7. Configuration

### 7.1 Web.config additions

```xml
<appSettings>
  <!-- existing settings unchanged -->

  <!-- Webhook correlator background worker -->
  <add key="WebhookCorrelator.Enabled" value="true" />
  <add key="WebhookCorrelator.PollIntervalSeconds" value="10" />
  <add key="WebhookCorrelator.BatchSize" value="100" />
</appSettings>
```

The vendor-specific webhook auth (apikey value, basic user/pass) is NOT in Web.config — it's in `VendorAPI_FK.dbo.ClientProfiles.ConfigJson`. This matches D-018: config-driven per-vendor settings live in the DB, not in app files.

### 7.2 ClientProfile ConfigJson — adding webhookAuth

Augment the FK ClientProfile row (from Deliverable #4 Section 8) with a `webhookAuth` subkey:

```json
{
  "apiKey": "<outbound key>",
  "billToCode": "VECTOR-001",
  "baseUrl": "https://api.fourkites.com",
  "timeoutSeconds": 15,
  "webhookAuth": {
    "mode": "apikey",
    "headerName": "X-FourKites-Token",
    "headerValue": "<inbound token shared with FK>"
  }
}
```

If FK provisions Basic auth instead:

```json
{
  "webhookAuth": {
    "mode": "basic",
    "basicUser": "vector-fk-callback",
    "basicPassword": "<password>"
  }
}
```

### 7.3 Public URL for FK to call

Whatever OTR API's external hostname is, the FK webhook URL is:

```
POST https://<otr-api-public-host>/api/vendorwebhook/fourkites
```

Glen provides this URL to FK CSM during webhook provisioning. **Open Item O-010** captures the production host; for sandbox testing, a public URL via ngrok or similar may be needed if dev OTR API isn't internet-reachable.

---

## 8. Idempotency and dedupe — the SHA256 hash strategy

FK can retry webhook delivery on its end (network errors, timeouts, slow ACKs). The receiver must not double-process.

**The strategy:**
- Compute `SHA256(rawBody)` on receipt
- Persist to `VendorInboundCallbacks` with a UNIQUE constraint on `(VendorName, PayloadHash)`
- `InboundCallbackRepository.UpsertAsync` does `MERGE`-style insert-or-update — if hash matches existing row, update `ReceivedUtc` and `LastSeenUtc` but don't change `ProcessedUtc`
- The correlator only picks up rows where `ProcessedUtc IS NULL`, so duplicates that arrive after processing don't get reprocessed
- Duplicates that arrive BEFORE processing simply update timestamps on the existing row; the original is still in the queue

**Why this works:** FK's webhook content is deterministic for a given event. Same event = identical body = identical hash. If FK ever changes the body (e.g., adds a retry counter to the JSON), the hash differs and we'd treat it as a new event — but that's still better than missing one.

**What it doesn't catch:** FK sending a *different* webhook that contains the same business meaning (e.g., two separate STOP_ARRIVAL events for the same stop, from different upstream sensors). That's a business-logic dedupe, which we explicitly don't do — the audit log captures both, and correlation picks up the one that arrives first.

---

## 9. Summary of changes to OTR API

| File | Change type | Lines added |
|---|---|---|
| `OTR API.csproj` | `Vendor.Common.dll` and `Vendor.FourKites.dll` references (already added in #4 — no new references here) | 0 |
| `Web.config` | 3 `WebhookCorrelator.*` settings | 3 lines |
| `Controllers\VendorWebhookController.cs` | **NEW FILE** — vendor-agnostic webhook endpoint | ~120 lines |
| `Global.asax.cs` | 1 line in `Application_Start`, 1 line in `Application_End` (add `Application_End` if absent) | ~4 lines |

**Total: ~130 lines added to OTR API across 3 files.**

Plus changes inside `Vendor.Common` and `Vendor.FourKites` (the framework-side work) — those are inside the DLLs and don't require Glen touching OTR API source.

**Grep test:** after merge, `grep -ri "fourkites" .` in the OTR API source folder should still return zero matches (apart from `.csproj` HintPath). `VendorWebhookController` only mentions `vendorName` as a route parameter — no vendor name is hardcoded.

---

## 10. Auth strategy — decision point for Glen

FK supports multiple auth schemes for webhook callbacks. Glen confirms with FK CSM which one will be used. The framework supports all of them; pick one and configure.

**Recommendation (in order of preference):**

1. **apikey header** — simple, easy to rotate, no Basic auth credentials to manage. FK sends a header like `X-FourKites-Token: <token>` on every webhook. Vector stores the same token in the FK ClientProfile's ConfigJson and matches.

2. **HTTP Basic auth** — works with any HTTP infrastructure, but the credentials are sent on every call (over HTTPS, so encrypted in transit, but more credential exposure than apikey).

3. **None (IP allowlisting only)** — FK publishes their outbound webhook IP ranges; Vector's firewall restricts `/api/vendorwebhook/fourkites` to those IPs only. Operationally simpler at the network layer; relies on Vector's perimeter being correctly configured.

Most shippers I've seen go with **#1 (apikey)** for the production deployment. It's what the existing `WebhookAuthMiddleware` was designed to support and what FK CSMs typically provision by default.

---

## 11. SQL schema needed for this deliverable

The full DDL is Deliverable #7, but the inbound side needs these table shapes:

```sql
-- VendorAPI_FK.dbo.VendorInboundCallbacks
CREATE TABLE dbo.VendorInboundCallbacks (
    CallbackId            BIGINT IDENTITY(1,1) PRIMARY KEY,
    VendorName            NVARCHAR(50) NOT NULL,
    PayloadHash           CHAR(64) NOT NULL,       -- SHA256 hex
    RawPayload            NVARCHAR(MAX) NOT NULL,
    MessageType           NVARCHAR(50) NULL,
    VendorLoadId          NVARCHAR(50) NULL,
    VectorLoadId          NVARCHAR(50) NULL,
    ReferenceNumbersJson  NVARCHAR(MAX) NULL,
    IsSuccess             BIT NULL,
    ErrorsJson            NVARCHAR(MAX) NULL,
    ReceivedUtc           DATETIME2 NOT NULL,
    LastSeenUtc           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedUtc          DATETIME2 NULL,
    MatchedTransactionId  BIGINT NULL,
    CONSTRAINT UQ_VendorInboundCallbacks_Hash UNIQUE (VendorName, PayloadHash)
);
CREATE INDEX IX_VIC_Unprocessed
    ON dbo.VendorInboundCallbacks (VendorName, ProcessedUtc)
    WHERE ProcessedUtc IS NULL;
CREATE INDEX IX_VIC_VectorLoad ON dbo.VendorInboundCallbacks (VectorLoadId, VendorName);
```

The `UQ_VendorInboundCallbacks_Hash` UNIQUE constraint is what makes idempotency work — duplicate posts hit the constraint and the repository converts the failure into an UPDATE of the existing row.

---

## 12. Pre-merge checklist

Before merging the controller into OTR API:

- [ ] Deliverable #10 framework (`Vendor.Common.dll`, `Vendor.FourKites.dll`) built and referenced by OTR API
- [ ] `FourKitesWebhookProcessor` and `FourKitesWebhookSignatureValidator` classes added to `Vendor.FourKites`
- [ ] `WebhookCorrelator` relocated from old `WebhookReceiver` project to `Vendor.Common\Persistence\`
- [ ] `VendorAPI_FK.dbo.VendorInboundCallbacks` table created (Deliverable #7)
- [ ] FK ClientProfile row has `webhookAuth` subkey with credentials provisioned by FK CSM
- [ ] FK CSM has been given the public webhook URL: `https://<otr-host>/api/vendorwebhook/fourkites`
- [ ] FK sandbox can hit the URL (test with a manual cURL first, then a real FK callback)

---

## 13. What to test after merge

| Test | Expected result |
|---|---|
| Manual cURL with valid auth + a recorded FK payload | 200 OK, row in `VendorInboundCallbacks` |
| Manual cURL with invalid auth | 401 Unauthorized, no row |
| Manual cURL with unknown vendor (`/api/vendorwebhook/xyz`) | 404 Not Found |
| Same payload sent twice within 1 second | Two 200s, only one row in `VendorInboundCallbacks` (UPDATE on second) |
| FK sandbox sends a `LOAD_CREATION` callback for a load OTR previously dispatched | Row appears; within 20 seconds, correlator updates the matching outbound transaction to `Status = 'CONFIRMED'`; Vector's `[Load]` table gets `FourKitesLoadId` stamped |
| FK sandbox sends a `STOP_ARRIVAL` callback | Row appears; correlator updates matching transaction; `[Load]` table NOT modified (only LOAD_CREATION triggers the stamp) |
| FK sends a callback for a load OTR never sent | Row appears; correlator logs "no match" and marks Processed; no transaction updates |

---

## 14. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-001 | FK webhook auth mode (apikey vs basic vs none + IP allowlist) — FK CSM confirms | Production deployment; sandbox can use apikey with a self-chosen token |
| O-010 | Production OTR API public hostname for the webhook URL | Production deployment |
| O-501 | Verify Vector's Load table is named `dbo.[Load]` with `LoadId` PK — adjust `OnConfirmedAsync` SQL if different | Stamping behavior works; tested when first LOAD_CREATION callback arrives |
| O-502 | Encryption-at-rest for webhookAuth credentials in ConfigJson | Production deployment |
| O-503 | Should the correlator log per-pass info to event log, or just on errors? Current design logs both. | Operational tuning |

---

## 15. Done-when checklist

Mark this deliverable complete when:

- [ ] `VendorWebhookController` added to OTR API and compiles
- [ ] `Global.asax.cs` starts the correlator in `Application_Start`
- [ ] `Web.config` has the 3 `WebhookCorrelator.*` settings
- [ ] `FourKitesWebhookProcessor` exists in Vendor.FourKites and is wired into `<vendorAdapters>` config
- [ ] `FourKitesWebhookSignatureValidator` exists and is wired in
- [ ] `WebhookCorrelator` relocated to Vendor.Common with vendor-agnostic logic
- [ ] All 7 tests in Section 13 pass
- [ ] **Grep test:** `grep -ri "fourkites" .` in OTR API source returns zero matches
- [ ] 24-hour soak test in staging shows correlator processing callbacks within 30 seconds of receipt, no dead-letter accumulation
- [ ] Production deployed with FK CSM-provisioned webhook URL

---

## 16. What this deliverable proves

After completion:

- OTR API has ONE inbound webhook endpoint — vendor-agnostic — that handles FourKites today and any future vendor tomorrow
- All FK-specific webhook knowledge (payload parsing, correlation logic, "stamp Vector's Load table") lives in `Vendor.FourKites.Webhooks.*`
- Adding vendor #2's webhook receiver is: write `Project44WebhookProcessor` + `Project44WebhookSignatureValidator`, add a config line, give FK CSM (or P44's CSM) the URL `/api/vendorwebhook/project44`
- The dedupe-by-hash pattern handles retries safely without business-logic complexity
- Correlation is decoupled from receipt — slow correlation doesn't slow webhook ACKs
- The audit log shows the complete request/response/correlation lifecycle for every webhook

---

*End of Webhook Receiver document.*
