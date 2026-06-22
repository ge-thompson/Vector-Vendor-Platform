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

**Status: RESOLVED via production testing (Tx 136) + FK docs extraction.**

FK's 4xx responses use one of two shapes depending on endpoint:

Load Create / Update / Delete (`/api/v1/tracking/*`):
```json
{
  "errors": ["Stops cannot have appointment time earlier than 4 days", "Driver phone is invalid"],
  "message": "",
  "requestId": "d8qruc6i9rjrcfre85cg",
  "statusCode": 400
}
```

Dispatcher Update (`/load/update/dispatcher-api/async`):
```json
{
  "requestId": "5aed0c8a-fb50-442c-bcce-aee0150587dc",
  "status": "400",
  "message": "Updates Array cannot be empty",
  "timestamp": "2023-09-14T17:53:49.356497"
}
```

Document-data may also use the `sub_errors[]` shape (per FK docs).

**Current handling:** errors are logged verbatim in `VendorOutboundTransactions.ResponseBody`. Future improvement: parse field-level messages from the `errors[]` array and surface them in `VendorOutboundTransactions.ErrorMessage`.

---

## O-006: Carrier-side Location / Status push to FK

**Status: RESOLVED via FK docs extraction (corrects earlier wrong-headed conclusion).**

**Original mistake:** prior version of this doc claimed "FK does its own truck tracking
once driverPhone is handed off; no FK endpoint exists for pushing TT-sourced location
or status updates IN." That was wrong. Vector is the registered carrier on these loads,
and pushing TT-sourced location + status updates to FK is the whole point of the
integration.

**Actual model:** there's a single endpoint that carries both location and status
updates (and ETA, temperature, etc.):

```
POST https://api.fourkites.com/load/update/dispatcher-api/async
auth: apikey header (same as Load Create)
```

Payload shape:
```json
{
  "updates": [{
    "timeZone": "UTC",
    "billToCode": "2215324",
    "identifierKeys": [{ "identifier": "<loadNumber>", "identifierType": "loadNumber" }],
    "loadUpdate": [{
      "locationUpdate": { "latitude": "...", "longitude": "...", "locatedAt": "<ISO 8601 with Z>" },
      "eventUpdate":    { "statusCode": "X1", "eventTimeStamp": "<ISO 8601 with Z>" }
    }]
  }]
}
```

Key shape facts:
- Lat/lon are **strings** in Decimal Degrees format.
- Timestamps are ISO 8601 **with Z** (different from stop appointment times in Load Create which omit the Z).
- Load is identified by `loadNumber` + `billToCode` — no FK loadId needed, so no cross-reference lookup on the location/status path.
- One `locationUpdate` and one `eventUpdate` per `loadUpdate` (no arrays).
- Returns HTTP 202 Accepted (async).
- Up to 200 updates per call (we send one per dispatch in Phase 1).

**Current adapter routing (after rev 2 rewrite):**
- `LocationReportedEvent` -> dispatcher endpoint with `locationUpdate` only
- `LoadStatusEvent` -> dispatcher endpoint with `eventUpdate` only
- `LoadStatusType.Delivered` -> sets `delivered: true` flag on eventUpdate so FK flips the load

**Status mapping:** FK accepts EDI 214 codes (X1, AF, X3, CD, D1, OA, X9). Vector's
`LoadStatusType` enum already maps to these via `LoadStatusMapper`.

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

**Status: PARTIALLY RESOLVED via FK docs extraction.**

FK documents 1 req/sec / 60 req/min per apikey on the Dispatcher Update endpoint.
Over-limit returns HTTP 429 with these response headers:
- `X-RateLimit-Limit-minute`
- `X-RateLimit-Remaining-minute`

Whether the Load Create endpoint shares the same 60/min counter or has its own
is still unconfirmed.

**Current handling:** local InMemoryRateLimiter throttles before HTTP. On 429 we
treat as transient retryable. Future improvement: read the headers FK returns and
adjust our local limiter accordingly.
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
