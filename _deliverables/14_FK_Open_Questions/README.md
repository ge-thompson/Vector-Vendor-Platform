# Open Questions for FourKites CSM / Dev Team

These were assumed during the FK adapter rewrite. Each assumption is workable
for dev testing but should be confirmed before production.

---

## O-001: Vector's SCAC / Service Provider ID

**Question:** What should we put in the `load.carrier` field of the Load Create
payload?  Per FK docs this is a `String` containing the carrier's SCAC or
Service Provider ID.

**Assumed:** `"VCTR"` (placeholder). Configurable in `ClientProfile.ConfigJson`
under `vectorScac` so we can change without code deploy.

**Why it matters:** Without the right value here, FK either rejects the load
(`400 Carrier is not specified`) or assigns it to the wrong carrier.

---

## O-002: Which FK environment for dev work?

**Question:** Is our dev API key (`OFX6BL85E0SC9W9SDHIEWTTPRFH8U`) valid against
`api.fourkites.com` (Production), `api-staging.fourkites.com` (Staging), or
both?

**Assumed:** **Staging** (`api-staging.fourkites.com`) is the right starting
target for dev work. The dev portal app is named "Vector TT Tracking
Development" which suggests dev-only credentials.

**Config knob:** `ClientProfile.ConfigJson.environment` = `"staging"` |
`"production"` | `"azure-staging"` | `"azure-production"`.

---

## O-003: Webhook authentication pattern

**Question:** Which auth pattern will FK use when calling Vector's webhook
endpoint?  Per FK docs the options are: Basic, API Key in a header, OAuth2,
or any combination of "Additional Headers."

**Assumed:** **API Key in a custom header** (`X-FK-Webhook-Key: <secret>`).
This is FK's most common pattern and the simplest to verify on our side.

**What we need from FK:**
- Confirmation of the header name they'll send
- The secret value to compare against
- Whether they also want to negotiate Basic or OAuth2 instead

The validator already supports `apikey` / `basic` / `none` schemes — change
without code deploy via `ConfigJson.webhookAuth.scheme`.

---

## O-004: FK static IP range for webhook whitelist

**Question:** What IP addresses or CIDR ranges does FK use when calling our
webhook endpoint?  Per FK docs these should be whitelisted at the network
layer as an additional verification mechanism.

**Assumed:** No allowlist enforced for dev. The validator code has a hook for
IP-based allow but it's empty until FK provides values.

**What we need from FK:** Static IP list or CIDR ranges for both Staging and
Production.

---

## O-005: Verbatim 4xx JSON body shape from tracking endpoints

**Question:** The FK docs publish an error table (`400 Load information is
empty`, `409 API call limit exceeded`, etc.) but not the actual JSON body
shape returned on a 4xx. The document-data endpoint shows a `sub_errors[]`
shape, but it's unclear if tracking endpoints follow the same.

**Assumed:** Errors are logged verbatim in
`VendorOutboundTransactions.ResponseBody`. The framework doesn't need to parse
them right now — it categorizes by HTTP status code, not body. Future
improvement: parse the body for field-level error messages and surface them
in `VendorOutboundTransactions.ErrorMessage`.

**What we need from FK:** A sample 4xx JSON body for the tracking endpoints.

---

## O-006: Are LoadStatusEvent / LocationReportedEvent useful to FK?

**Question:** FK's model appears to be: once we hand them `trackingInfo`
(driver phone, truck #, trailer #), FK tracks the truck themselves via the
CarrierLink app or carrier integrations and pushes status/location updates
BACK to us via webhook. The FK Tracking API does not expose an endpoint for
pushing TT-sourced status or location updates INTO FK.

**Assumed:** The FK adapter **skips** LoadStatusEvent and LocationReportedEvent
(returns `VendorOperationResult.Skipped("FK does its own tracking once handoff
is complete")`). These events still fire in the framework — other vendors may
want them — but FK gets `0` events from SendStatus dispatch.

**What we need from FK:** Confirmation that this is correct. If FK does want
TT-sourced status updates, they should point us at the endpoint and we'll
wire them in.

---

## O-007: Update timing — when does Update become Create?

**Question:** Our integration fires `LoadAssignedEvent` for both `TrackLoad`
(driver first assigned) and `UpdateTrackLoad` (driver reassigned). FK's API
uses POST for Create, PATCH for Update — different methods, different URLs.

**Assumed:** The adapter checks `LoadCrossReference` for an existing FK
`loadId` keyed by `(VectorLoadId, FourKites)`:
- If no row exists -> POST `/api/v1/tracking` (Create). Capture FK's
  returned `loadId` into the cross-reference on 2xx.
- If a row exists -> PATCH `/api/v1/tracking/{fkLoadId}` (Update). Body uses
  `simpleUpdate: false` (partial update — only fields we're changing).

**Why it matters:** If we POST when we should PATCH, FK creates a duplicate
load. If we PATCH when we should POST, FK returns 404. The cross-reference
table is what keeps them straight.

---

## O-008: Document upload format

**Question:** Our `DocumentAvailableEvent` adapter currently builds a metadata
JSON. FK's `/document-data/upload` endpoint expects a base64-encoded file in
`documents[].base64_content` — not multipart.

**Assumed:** The adapter builds the base64 JSON body, **but the file bytes
themselves aren't yet wired into the event**. We pass through metadata only
for now. The base64 content path activates when the FBS-side POD capture
flow lands (Phase 2 DocumentAvailableEvent enrichment).

**What we need from FK:** Confirmation of which `document_type` codes match
our internal POD / BOL / packing slip categorization. Per FK docs the codes
are BLD, PSD, BL, PS, CD, DR, IV, WC, WI, WP, FB, PO, OD, FA, LR.

---

## O-009: Rate limit headers

**Question:** FK docs state a 1 req/sec limit on Create. Does FK return rate-
limit headers in response (`X-RateLimit-Remaining`, `Retry-After`, etc.) we
can use to back off intelligently?

**Assumed:** Local InMemoryRateLimiter throttles us to 1 req/sec before we
even hit FK. On 429 from FK (which shouldn't happen if our local limiter
works), we treat it as a transient retryable failure with no header parsing.

**What we need from FK:** Sample 429 response (if any).

---

## O-010: Webhook payload retry semantics

**Question:** FK docs state 5xx retries with exponential backoff (1m, 15m,
1h, 24h), no retry on 4xx. Our webhook endpoint should return 200 on
successful processing.

**Assumed:** Our webhook endpoint always returns 200 on a syntactically valid
payload (regardless of whether internal processing succeeds). Internal errors
log to AuditLogs and process out-of-band. Worst case is duplicate processing,
which `LoadCrossReference` handles idempotently.

---

## O-011: haulType value for Vector loads

**Question:** FK's `load.haulType` array requires values like `"inbound_load"`,
`"outbound_load"`, `"brokered_load"`, etc. Vector is a freight broker.

**Assumed:** `["brokered_load"]` for all loads. Configurable in ConfigJson
under `defaultHaulType` if needed.

**What we need from FK:** Confirmation that `brokered_load` is the right value
for Vector's role in the supply chain.
