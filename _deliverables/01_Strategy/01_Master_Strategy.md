# Vendor API Integration Platform — Master Strategy

**Document:** Deliverable #1 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Status:** Foundation document — read first, reference throughout
**Owner:** Glen Thompson

---

## 0. How to use this document

This is the **strategy and architecture document** for the Vendor API Integration Platform. It establishes the *why*, the *shape*, and the *decision log*. Other deliverables (the code, the SQL, the upgrade playbook, the insertion points, the framework design) reference back to this document.

**If you are a new developer or a fresh Claude session arriving cold:** read this document end to end before opening any code. Every architectural choice has a reason; every reason is recorded here. The intent is that you can pick up this work without needing to ask Glen for the backstory.

**If you are revisiting this after time away:** Section 4 (Decision Log) is the fastest way to refresh on why things are the way they are.

---

## 1. Executive summary

Vector is building a reusable API integration platform. The first integration is **FourKites** (a supply chain visibility platform), driven by location and status events flowing from **TruckTools** through an existing webservice called **OTR API**. The same platform will absorb POD uploads from a VB.NET application, and ultimately replace selected EDI traffic from **Vector FBS** (the freight broker system).

The platform is being built as a **framework that can be sold to other Vector clients**, who will use it to integrate with whichever visibility / TMS / EDI-replacement vendors they need. FourKites is the reference implementation; the second vendor (when it appears) drops in via configuration, not new code.

### Key facts

| Fact | Value |
|---|---|
| First vendor | FourKites |
| First triggering source | TruckTools webhook events into OTR API |
| Second source | VB.NET application (POD upload) |
| Third source (Phase 2) | Vector FBS (status events, EDI replacement) |
| Universal correlation key | `VectorLoadId` |
| Vendor SLA requirement | 95% success rate (FourKites contractual) |
| Database server | `10.10.9.10\SQLEXPRESS12` (local LAN) |
| New database | `VendorAPI_FK` |
| .NET Framework target | 4.8.1 (after OTR API upgrade) |
| Auth model (outbound to FourKites) | `apikey` header |
| Auth model (inbound from FourKites) | TBD — see Open Items |

---

## 2. The big picture

### 2.1 What exists today

```
                        ┌──── 10.10.9.10\SQLEXPRESS12 ────┐
                        │  VectorOTR      (hostjson)       │
                        │  VectorOTR_TT   (hostTT)         │
                        │  VectorOTR_FBS  (hostFBS)        │
                        │  VectorFBS      (hostRate)       │
                        └──────────────────────────────────┘
                                       ▲
                                       │ ADO.NET
                                       │
        ┌──────────────────────────────┴──────────────────────────────┐
        │  OTR API  (.NET Framework 4.6.1 currently, IIS)              │
        │                                                              │
        │  /api/truckertools/*           ← Vector posts loads          │
        │  /api/truckertoolstracking/*   ← TruckTools posts status     │
        │  /api/loads/* /api/drivers/*   ← other endpoints             │
        └──────────────────────────────────────────────────────────────┘
              ▲                                       ▲
              │ HMAC-auth'd                           │ HMAC-auth'd
              │                                       │
        Vector FBS                                TruckTools
        (submits loads,                           (sends GPS + status
         pulls status updates)                     via webhook)
```

OTR API is the existing bridge between Vector FBS and TruckTools. It receives matched-trip loads from Vector, forwards them to TruckTools, receives status/location webhooks back from TruckTools, and exposes those status updates for Vector to pull.

The OTR API project is at `C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\OTR API\` and is under SVN source control.

### 2.2 What we are building

```
                                                  ┌──── VendorAPI_FK ────┐
                                                  │  (new database)       │
                                                  │                       │
                                                  │  OutboundTransactions │
                                                  │  InboundCallbacks     │
                                                  │  ClientProfiles       │
                                                  │  LoadCrossReference   │
                                                  └───────────────────────┘
                                                              ▲
                                                              │
        ┌─────────────────────────────────────────────────────┴────────────┐
        │  OTR API  (UPGRADED to .NET Framework 4.8.1)                     │
        │                                                                    │
        │  Existing controllers (UNCHANGED behavior, tee-offs added):       │
        │  - PostLoad()              → CreateShipment + tee-off to FK       │
        │  - TrackLoad()             → AssignmentUpdate tee-off             │
        │  - SendStatus()            → LocationUpdate + EventUpdate tee-off │
        │  - CancelLoadTracking()    → LoadInfoUpdate(StopTracking) tee-off │
        │                                                                    │
        │  NEW controller:                                                  │
        │  /api/fourkiteswebhook/receive                                    │
        │                                                                    │
        │  References: Vendor.Common.dll + Vendor.FourKites.dll             │
        └────────────────────────┬────────────────────────────────────────┘
                                 │
                ┌────────────────┴────────────────┐
                ▼                                  ▼
        ┌─────────────────┐              ┌──────────────────┐
        │ Vendor.Common   │              │ Vendor.FourKites │
        │ (the framework) │◄────────────►│ (FK implementation)│
        └─────────────────┘              └──────────────────┘
                ▲                                  ▲
                │                                  │
                │           also referenced by:    │
                │                                  │
        ┌───────┴────────┐              ┌─────────┴───────────┐
        │  VB.NET app    │              │ Vector FBS (Phase 2)│
        │  (POD upload)  │              │ (replaces EDI)      │
        └────────────────┘              └─────────────────────┘
                                                  │
                                                  │
                                              POST JSON
                                                  │
                                                  ▼
                                        ┌────────────────────┐
                                        │ FourKites API      │
                                        │ api.fourkites.com  │
                                        └────────────────────┘
                                                  │
                                                  │ webhook callbacks
                                                  │
                                                  ▼
                                         /api/fourkiteswebhook
                                          (back to OTR API)
```

### 2.3 Phased rollout

| Phase | What ships | Who depends on it | Status |
|---|---|---|---|
| **Phase 0** | Framework foundation: `Vendor.Common` + refactored `Vendor.FourKites` + `VendorAPI_FK` schema | Everything downstream | Not started |
| **Phase 1** | OTR API tee-off (TruckTools → FourKites) + VB.NET POD upload | First production traffic to FourKites | Not started |
| **Phase 2** | Vector FBS calls the DLL for selected events (rate confirm, pickup changed, etc.); EDI is selectively replaced (decision logic in FBS) | Reduces EDI dependency | Designed only |
| **Phase 3** | Multi-vendor expansion: ClientProfile-driven routing, second vendor onboarding | Resale to other Vector clients | Designed only |

Phase 0 is a separate phase from Phase 1. The framework foundation has to land first; the integration work uses it. This ordering is why "framework first" is the right call given the resale intent — see Section 5.

---

## 3. Architecture principles

These principles govern every decision below. Refer back here when in doubt.

### 3.1 The DLL is dumb

The vendor DLLs (`Vendor.FourKites`, future `Vendor.Project44`, etc.) know how to talk to one vendor. They do not know about business rules, EDI, shipper preferences, or routing decisions. Calling code (OTR API, FBS, VB.NET app) decides *what* to send and *when*. The DLL serializes JSON, signs requests, handles retries, and returns responses.

**Why:** This is the only way the framework can be resold. A Vector-business-rules-baked-in DLL is useless to another client.

### 3.2 The framework owns cross-cutting concerns

Auditing, rate limiting (where shared), cross-reference resolution (Vector ID ↔ vendor ID), webhook signature validation patterns, error classification, and retry policy live in `Vendor.Common`. Each vendor DLL specializes the pattern for its specific vendor.

**Why:** When vendor #2 arrives, 80% of the plumbing is already written and tested.

### 3.3 Phase 1 must work standalone

The platform must deliver value with one vendor and one caller (OTR API → FourKites). We don't wait for Phase 2 or Phase 3 to validate the design. If Phase 1 doesn't ship, nothing else matters.

**Why:** Scope discipline. Frameworks-that-never-ship are an anti-pattern.

### 3.4 VectorLoadId is the universal correlation key

Every record in every table, every payload sent to any vendor, every webhook received back, every audit log entry — all carry `VectorLoadId`. Other identifiers (TruckToolsLoadId, vendor's internal load ID) are secondary metadata.

**Why:** Answering "tell me everything that happened for load 12345" must be a single SQL query, not a join across four systems.

### 3.5 Routing decisions live where the business rules live

The decision "should this event go to FourKites via API or via EDI?" is made by **Vector FBS**, not by the DLL. The DLL just sends what it's asked to send. FBS owns shipper-by-shipper, event-by-event policy.

**Why:** Business rules change quarterly. Plumbing changes yearly. They don't belong in the same module.

### 3.6 Audit is non-negotiable and exhaustive

Every outbound call, every response received, every inbound webhook is persisted. We do not rely on application logs. The audit log is the system of record for "what did we send / receive."

**Why:** 95% SLA can't be measured without complete data. Support tickets to FourKites require requestId lookups. Compliance and customer disputes require evidence.

### 3.7 Configuration over code

Adding a new shipper to a vendor, enabling/disabling an event type for a shipper, switching API/EDI for a shipper — all should be SQL config changes, not deployments.

**Why:** Operations should not require developers.

### 3.8 Fail loud, not silent

If a FourKites push fails three retries, that has to surface — to a monitoring dashboard, to an alert, to *something* visible to operations. Silent failure is how you discover at month-end that you were at 87% success and lost the contract.

**Why:** The 95% SLA implies an active monitoring requirement, not just a logging requirement.

---

## 4. Decision log

Every decision made in conversation, with rationale. When you wonder *why* something is the way it is, the answer is here.

### D-001: First vendor is FourKites
- **Date:** Pre-existing
- **Decision:** FourKites is the first integration; reference implementation for the framework
- **Rationale:** Existing contractual relationship, defined need, available API documentation

### D-002: Use API instead of (or alongside) EDI for FourKites
- **Date:** Earlier in project
- **Decision:** Standardize on REST API for FourKites communication
- **Rationale:** Faster than EDI, cheaper, richer feedback loop, easier to add event types

### D-003: Solution targets .NET Framework 4.8.1
- **Date:** Earlier in project
- **Decision:** All projects target .NET Framework 4.8.1
- **Rationale:** Matches existing Vector FBS shop; modern TLS support; modern HttpClient
- **Implication:** OTR API must be upgraded from 4.6.1 to 4.8.1 — see D-013

### D-004: Architecture is "fully separate service" originally
- **Date:** Earlier in project
- **Decision:** Built `FourKitesIntegration.OutboundService` and `WebhookReceiver` as Windows Services
- **Status:** **Superseded by D-014** — webhook receiver moves into OTR API
- **Rationale at the time:** Decoupling, independent scaling
- **Why superseded:** Glen wants single deployment for Phase 1, single audit DB, fewer moving parts

### D-005: Vector → OutboundService over internal HTTP (not SQL outbox)
- **Date:** Earlier in project
- **Decision:** Vector FBS POSTs JSON to the OutboundService
- **Status:** **Reconsidered in current conversation** — Phase 2 has Vector FBS referencing `Vendor.FourKites.dll` directly, bypassing the OutboundService entirely (D-019)

### D-006: Real FourKites endpoint is dispatcher-api/async
- **Decision:** Use `POST https://api.fourkites.com/load/update/dispatcher-api/async`
- **Rationale:** Validated against FourKites' actual current API documentation
- **Note:** An earlier attempt (May 26 evening) accidentally targeted a legacy endpoint `tracking-api.fourkites.com` — wrong endpoint, wrong auth (Basic), wrong payload shape. That work was discarded. See `_deliverables\99_Archive\` if retained.

### D-007: Auth to FourKites is `apikey` header
- **Decision:** No OAuth, no Basic, no Digest — just an apikey header
- **Rationale:** Matches FourKites' documented current API

### D-008: VectorLoadId is the universal correlation key
- **Date:** Current conversation
- **Decision:** Every table, every payload, every log entry uses `VectorLoadId` as the primary correlation
- **Rationale:** Glen explicitly required: "VectorID is absolutely necessary, truck tool ID is secondary"
- **Implication:** Cross-reference table needed to resolve TruckToolsLoadId → VectorLoadId for inbound TT webhooks (already solved — TT payload's `loadNumber` field IS the VectorLoadId since OTR API passes it through that way)

### D-009: New database `VendorAPI_FK` on the existing SQL Express server
- **Date:** Current conversation
- **Decision:** Create a new database rather than adding tables to `VectorOTR_TT` or `VectorOTR`
- **Rationale:** Clean separation; doesn't pollute existing schemas; easier to back up / restore independently; easier to grant read-only access to a reporting user

### D-010: OTR API hosts the inbound webhook endpoint (Option C)
- **Date:** Current conversation
- **Decision:** Add `FourKitesWebhookController` to OTR API; webhook processing logic lives in `Vendor.Common.dll`
- **Rationale:** Single deployment, single audit database, single set of credentials to manage, OTR API is already internet-reachable from prior app backend usage
- **Open concern:** Auth scheme for FourKites callbacks — OTR API uses `[HMACAuthentication]` filter on existing controllers, but FourKites won't sign callbacks with Vector's HMAC. See Open Items.

### D-011: 95% success rate is a hard SLA
- **Date:** Current conversation
- **Decision:** Monitoring and dashboarding are first-class requirements, not afterthoughts
- **Rationale:** Glen: "Fourkites requires a 95% success rate. I have to be able to monitor that."
- **Implication:** Phase 1 includes a success-rate dashboard (Deliverable #8)

### D-012: "Tell me everything sent and received for a load" must be one query
- **Date:** Current conversation
- **Decision:** Audit log is comprehensive and indexed by VectorLoadId
- **Rationale:** Glen: "I have to be able to tell everything that was sent and received on a load"

### D-013: Upgrade OTR API from 4.6.1 to 4.8.1
- **Date:** Current conversation
- **Decision:** Accept the upgrade risk to keep one .NET version across all projects
- **Alternatives considered:**
 - Retarget Vendor.FourKites to 4.6.1 → rejected, blocks modern HttpClient/TLS features
 - Build Vendor.FourKites as .NET Standard 2.0 → rejected, adds complexity for no benefit
- **Mitigation:** Deliverable #3 is the upgrade playbook with test plan and rollback procedure
- **Owner of the upgrade:** Glen exclusively (per Glen: "I want to be the only person to make changes to the OTRAPI")

### D-014: Only Glen modifies OTR API
- **Date:** Current conversation
- **Decision:** Claude/AI never writes to OTR API source files; only proposes changes in insertion-points documents
- **Rationale:** Glen: "But I want to be the only person to make changes the OTRAPI"
- **Implication:** All OTR API deliverables show "existing code → new code beside it" for hand-merging

### D-015: VB.NET app references DLL directly for POD upload
- **Date:** Current conversation
- **Decision:** The VB.NET application takes a direct reference to `Vendor.FourKites.dll`; it does NOT call OTR API
- **Rationale:** POD upload is a VB.NET-owned business event, no need to route through OTR API
- **Implication:** DLL public surface must be VB.NET-friendly (no C# 9-isms leaking out)

### D-016: TruckTools status code → EDI 214 mapping derived from audit log
- **Date:** Current conversation
- **Decision:** Glen will provide TruckTools status code samples from the existing audit log (TT docs as backup)
- **Rationale:** The audit log has real production data showing what codes TT actually sends; faster than getting docs from TruckTools

### D-017: Build all three phases now, not just Phase 1
- **Date:** Current conversation
- **Decision:** All three phases get designed in this engagement, even though Phase 2 and 3 aren't built code-wise
- **Rationale:** Glen: "if I have to pass this off I lose a lot of time and effort - I [want to] build all three." Context loss between sessions is more expensive than the extra design time now.

### D-018: Future-proof naming from day one
- **Date:** Current conversation
- **Decision:** Rename `FourKitesIntegration.Core` → `Vendor.FourKites`; new database is `VendorAPI_FK`; audit tables have a `VendorName` column
- **Rationale:** Zero cost today; expensive to refactor once references are entrenched

### D-019: Vector FBS calls the DLL directly in Phase 2 (no OutboundService middleman)
- **Date:** Current conversation
- **Decision:** Phase 2 has FBS reference `Vendor.FourKites.dll` directly, bypassing any HTTP middleman service
- **Rationale:** Simpler; the DLL already handles retries, rate limiting, and audit logging; no need for a separate hop
- **Note:** This deprecates the original `FourKitesIntegration.OutboundService` Windows Service. We will keep its DispatchController as reference for the HTTP-envelope pattern but not deploy it.

### D-020: Framework-first build (reverses earlier "Rule of Three" stance)
- **Date:** Current conversation
- **Decision:** Build `Vendor.Common` framework BEFORE we have a second vendor
- **Rationale:** Glen explicitly stated the framework is part of the value proposition (resale to other Vector clients). Premature abstraction is the wrong concern when the abstraction itself is the product.
- **Tradeoff accepted:** Some `Vendor.Common` abstractions may need revision when vendor #2 arrives. We mitigate by pressure-testing every abstraction against a plausible second vendor (project44).

### D-021: API/EDI switching policy lives in FBS, not the DLL
- **Date:** Current conversation
- **Decision:** Vector FBS owns the decision logic for "API or EDI for this event for this shipper"
- **Rationale:** Glen: "the decision on API or EDI will be in FBS not this dll." Keeps the DLL business-rule-free and sellable to other clients.

### D-022: Documentation is self-contained for fresh sessions
- **Date:** Current conversation
- **Decision:** Every deliverable must make sense to a new developer or Claude session with zero prior context
- **Rationale:** Glen: "All this needs documentation so another chat can move forward without a history story from me"
- **Implication:** Decision logs, glossaries, explicit file paths, no assumed knowledge

### D-023: Billable hours tracking
- **Date:** Current conversation
- **Decision:** Maintain a worklog file recording what was produced per session
- **Rationale:** Glen: "we keep track of our hours and work done, this all has to be billed for"
- **Implementation:** `_deliverables\_worklog.md` updated as work proceeds

### D-026: Database name `VendorAPI_FK` — FK officially means "FrameworK", not "FourKites"
- **Date:** Current conversation, build phase
- **Decision:** Keep the database name `VendorAPI_FK`. Officially read the `_FK` suffix as **FrameworK**, not FourKites.
- **Why this came up:** Glen asked why the audit/config database was named with what looked like a FourKites-specific suffix when everything else in the framework is vendor-agnostic. Fair question — Deliverable #7's original justification ("_FK is transitional, could mean either") was weak.
- **Resolution path considered:**
  - **Rename** to `VectorVendorAPI` or `VectorAPI_Status` — cleaner long-term but adds churn across 9 SQL scripts, 6 deliverable docs, and several config samples
  - **Keep + redefine** — acknowledge FK as a framework acronym, document it once, move on
- **Chose to keep + redefine** because: zero code/script changes needed; the framework-first intent has been clear in every deliverable; renaming a database that hasn't been deployed anywhere doesn't add value commensurate with the documentation churn.
- **What this means going forward:**
  - Database name `VendorAPI_FK` is canonical. Read aloud as "Vendor API, Framework" not "Vendor API for FourKites".
  - If a future reader is confused, the glossary in the Master README points them here.
  - Adding vendor #2 (project44, etc.) does NOT require renaming the database. The name is already vendor-agnostic.
- **What this does NOT mean:** the `FK` token doesn't appear in any framework code paths, table columns, or class names. Those are all vendor-agnostic by design (`VendorName` columns, `IVendorAdapter`, etc.). Only the database file itself carries the `FK` acronym.

### D-025: Promote framework design ahead of OTR API insertion points (course correction)
- **Date:** Current conversation, mid-evening
- **Decision:** Reorder — design `Vendor.Common` framework (Deliverable #10) BEFORE rewriting Deliverable #4 (OTR API insertion points). Original execution order had #4 calling a FourKites-specific wrapper class (`FourKitesTeeOff`); revised order has #4 calling the vendor-agnostic `VendorDispatcher`.
- **Why this change:** Glen reviewed Deliverable #4 (FK-direct version) and asked which parts were framework vs FK-specific. Honest answer: almost everything was FK-specific. That contradicted D-020 (framework-first build, resale intent). Continuing FK-direct would have meant the OTR API code couldn't be shown to a future customer as a reference implementation of the resale-able framework.
- **What changed in the design:**
  - Introduced vendor-agnostic internal event types (LoadCreatedEvent, LoadAssignedEvent, LocationReportedEvent, LoadStatusEvent, LoadTrackingStoppedEvent, DocumentAvailableEvent)
  - `IVendorAdapter` contract replaces direct vendor client calls
  - `VendorDispatcher` becomes the only API callers see
  - `ClientProfile` table drives per-shipper, per-event vendor routing
  - TruckTools status code → EDI 214 mapping moved out of OTR API into `Vendor.FourKites.Adapter` (OTR API only translates to vendor-agnostic LoadStatusType)
- **Tradeoff accepted:** Some risk of getting abstractions wrong with only one concrete vendor. Mitigated by pressure-testing every abstraction against project44 as a hypothetical second vendor (see Deliverable #10 Section 9). Where abstractions might need revision when vendor #2 arrives, the additions will be additive (new enum values, new events) rather than rewrites.
- **Deliverable status changes:**
  - Deliverable #10 (Framework Design) — promoted ahead, completed
  - Deliverable #4 (OTR Insertion Points) — superseded version archived, rewrite pending
- **What Claude did wrong here:** Drifted back to "build the simple thing" instinct despite Glen explicitly setting the framework-first direction in D-020. Glen caught it by asking the right question ("which part is framework vs FK-specific?"). Process improvement: when in doubt between framework-first and pragmatic-first, framework-first per D-020 — don't re-litigate.

### D-024: One DLL per vendor (not split by direction or audience)
- **Date:** Current conversation
- **Decision:** `Vendor.FourKites.dll` is a single assembly containing outbound client, DTOs, webhook validator/parser, and mapping. Referenced as-is by OTR API (webservice), FBS (desktop), and the VB.NET POD app.
- **Alternatives considered:**
  - Split by direction (`Client.dll` + `Webhooks.dll`) — rejected: webhook DTOs are needed by FBS to read audit log records, defeating the split
  - Split by audience (full vs Lite) — rejected: code duplication, version drift risk, bad resale story ("reference these two if webservice, those two if desktop...")
- **Rationale:** Unused code in a referenced library doesn't execute, doesn't start listeners, doesn't add meaningful footprint. Conceptual cleanliness is preserved by internal folder organization (`Client/`, `Models/Outbound/`, `Models/Webhooks/`, `Webhooks/`, `Mapping/`). One vendor = one DLL is the cleanest mental model for the resale story.
- **Glen's concern at decision time:** "is it ok to have the webhooks in a dll that FBS will also call?" Answered: yes — passive helper classes never instantiated by desktop callers; no port binding, no transitive dependencies FBS doesn't already have, no security surface increase.
- **Implication:** Folder organization inside the DLL must make the audience boundary visible. `Webhooks/` subfolder is webservice-only by convention; XML doc comments on those classes call this out.

---

## 5. Why "framework first" is right for this engagement

This is worth its own section because it reverses standard advice and the reasoning matters.

**Standard advice (Rule of Three):** Don't build abstractions until you've written something three times. Two examples isn't enough to know which axis to abstract on. Premature abstraction tends to encode the wrong assumptions.

**Why standard advice doesn't apply here:**

1. **The framework IS the product.** When Vector sells this to another client, they're not buying "a FourKites integration" — they're buying "the ability to integrate with whatever vendor they need." The framework is the value proposition. Building "FourKites first and refactor later" means there's nothing to sell during the refactor period.

2. **The cost of getting it slightly wrong is lower than the cost of refactoring.** If `Vendor.Common` makes a few wrong assumptions and vendor #2 forces a revision, that's a refactor of one DLL. If we ship FourKites without framework structure and then try to extract framework, that's a refactor of FourKites + new vendor + all the calling code that referenced FourKites types directly.

3. **We have one concrete vendor and a plausible hypothetical second vendor.** FourKites is real. Project44 (the obvious next vendor — same product category) is well-documented and well-understood in the industry. We can design `Vendor.Common` against both, even though only FourKites is built. Two examples (one real, one designed-against) is better than waiting for two real examples.

4. **The cost of premature abstraction is paid by developers; the cost of resale-readiness is paid by sales.** The wrong tradeoff for an internal tool. The right tradeoff for a product.

**What we will do to keep premature abstraction risk low:**

- Every `Vendor.Common` abstraction must answer the question: "would this work for project44?" If not, it's wrong. Document the answer in code comments.
- `Vendor.Common` starts minimal. We don't pre-build abstractions for things we don't yet know we need.
- Vendor-specific code stays in `Vendor.FourKites`. When in doubt, keep it in the vendor DLL until a second vendor forces it up.
- The `IVendorClient` interface stays small. Big interfaces are hard to implement for the next vendor.

---

## 6. The framework — Vendor.Common conceptual design

This section establishes *what* goes in `Vendor.Common` and *what stays vendor-specific*. The detailed class design is Deliverable #10; this section is the conceptual frame.

### 6.0 One DLL per vendor (see D-024)

Each vendor gets a single assembly. `Vendor.FourKites.dll` contains everything FourKites-specific: outbound client, all DTOs, webhook validator/parser, and mapping. The same DLL is referenced unchanged by webservice callers (OTR API), desktop callers (FBS, VB.NET POD app), and any future caller. Unused code in a library is inert; passive helper classes never instantiated by desktop callers cost nothing at runtime.

**Folder layout inside the DLL** preserves the audience boundary visually:

```
Vendor.FourKites.dll
├── Client/                          ← outbound, used by everyone
├── Models/
│   ├── Outbound/                    ← used everywhere
│   └── Webhooks/                    ← shapes used to parse inbound AND to read audit log rows
├── Webhooks/                        ← signature validator + payload parser; webservice-only callers
└── Mapping/                         ← Edi214Mapper, etc.
```

### 6.1 What lives in Vendor.Common

| Component | Why it's common |
|---|---|
| `IVendorClient` interface | Marker interface so callers can write polymorphic code |
| `VendorTransaction` POCO | Audit log row shape — every vendor's outbound calls log the same fields |
| `VendorWebhookCallback` POCO | Inbound webhook log row shape |
| `OutboundTransactionRepository` | Persists transactions to `VendorAPI_FK.OutboundTransactions` |
| `InboundCallbackRepository` | Persists webhooks to `VendorAPI_FK.InboundCallbacks` |
| `LoadCrossReferenceRepository` | Resolves VectorLoadId ↔ vendor-internal-id |
| `ClientProfile` POCO | Per-shipper, per-vendor config |
| `ClientProfileRepository` | Loads ClientProfiles from `VendorAPI_FK.ClientProfiles` |
| `VendorDispatcher` | Routes events to the right vendor(s) based on ClientProfile |
| `IRateLimitTracker` interface | Each vendor implements its own; common interface for monitoring |
| `IWebhookSignatureValidator` interface | Same — each vendor's auth differs; common shape |
| `RetryPolicyFactory` | Polly-based retry policies; vendor-tuned but pattern is common |
| `VendorErrorClassifier` | Classifies HTTP responses into Transient / Permanent / RateLimit / Unknown |
| `SuccessRateCalculator` | Computes rolling-window success rate per vendor / per shipper / overall |

### 6.2 What stays vendor-specific (e.g. in Vendor.FourKites)

| Component | Why it's vendor-specific |
|---|---|
| `FourKitesClient` | Implements `IVendorClient`; knows FourKites' specific endpoints |
| All DTOs (`DispatcherBatch`, `LocationUpdate`, etc.) | Each vendor has its own payload schema |
| `FourKitesRateLimitTracker` | FourKites has 60/min; project44 will have something different |
| `FourKitesWebhookValidator` | FourKites' webhook auth differs from any other vendor's |
| `FourKitesAuthHandler` | The apikey-header logic |
| `Edi214Mapper` | Specific to FourKites' use of EDI 214 codes for status |

### 6.3 The ClientProfile abstraction

This is the heart of the multi-vendor capability. A `ClientProfile` row says:

> "For shipper ACME, send these event types to FourKites: location, status, POD. Send these event types to project44: location only. Use this API key for FourKites. Use this API key for project44."

Callers ask the framework: "I have this event for VectorLoadId X. Where does it go?" The framework looks up the load's shipper, finds the ClientProfile, and dispatches to each configured vendor.

**Shape (conceptual):**

```
ClientProfile
├── ShipperCode (e.g. "ACME")
├── VendorName (e.g. "FourKites")
├── ApiKey (encrypted at rest)
├── BaseHost (vendor-specific)
├── BillToCode (FourKites-specific, in metadata bag)
├── EnabledEvents (e.g. "Location,Status,POD")
├── IsActive
└── Metadata (JSON, vendor-specific extras)
```

A shipper that uses two vendors has two ClientProfile rows.

### 6.4 What "adding a new vendor" looks like (the test of the framework)

When vendor #2 arrives (let's say project44), the work is:

1. Create new project `Vendor.Project44` (copy `Vendor.FourKites` layout)
2. Implement `Project44Client : IVendorClient`
3. Implement `Project44RateLimitTracker` and `Project44WebhookValidator`
4. Write DTOs for project44's payload shapes
5. Write the event mapper (Vector internal events → project44 events)
6. Add ClientProfile rows for shippers using project44
7. Deploy the new DLL alongside `Vendor.FourKites.dll`

**Not in that list:** changes to `Vendor.Common`, changes to OTR API, changes to FBS, changes to the VB.NET app, changes to the audit database, changes to the dispatcher. If we find ourselves doing any of those when vendor #2 arrives, the framework abstraction was wrong and we revise it.

---

## 7. Phase-by-phase scope

### 7.1 Phase 0 — Framework foundation

**Deliverables:** #2, #3, #7

Build `Vendor.Common` and refactor `FourKitesIntegration.Core` → `Vendor.FourKites`. Upgrade OTR API to .NET 4.8.1. Create `VendorAPI_FK` database with the audit schema, ClientProfile schema, and cross-reference schema.

**Done when:**
- `Vendor.Common.dll` builds and unit tests pass
- `Vendor.FourKites.dll` builds and references `Vendor.Common`
- `VendorAPI_FK` database created and schema verified
- OTR API upgraded to 4.8.1, all existing endpoints still functional in staging

### 7.2 Phase 1 — OTR API tee-off + VB.NET POD

**Deliverables:** #4, #5, #6, #8

Add five tee-off insertion points in OTR API (Glen executes the merges). Build the inbound webhook controller in OTR API. Build the VB.NET POD upload sample. Build the success-rate dashboard.

**Done when:**
- TruckTools status webhooks result in FourKites location + event updates
- Webhooks from FourKites are received, parsed, and persisted in `VendorAPI_FK.InboundCallbacks`
- VB.NET app can upload a POD and confirm via FourKites Get Document API
- Dashboard query returns rolling 7-day success rate ≥ 95% in staging

### 7.3 Phase 2 — Vector FBS replaces selected EDI

**Deliverable:** #9 (design only)

FBS references `Vendor.FourKites.dll` directly. For each event type (rate confirm, pickup changed, etc.), FBS has a switchboard (config in FBS, not in the DLL) that decides: API, EDI, both, or neither. Rollout is shipper-by-shipper, event-type-by-event-type.

**Critical constraint:** No duplicate sends. If an event goes via API, it does NOT go via EDI for the same load+event.

**Design questions still open:**
- Where in FBS does the switchboard live? (TBD — Glen's call when we get to it)
- How does FBS know if FourKites accepted vs rejected an API send, in time to fall back to EDI? (Likely: it doesn't — fail loud, don't fall back automatically)
- Migration sequencing per shipper

### 7.4 Phase 3 — Multi-vendor + resale

**Deliverable:** #10 (design only)

When the second vendor arrives, the work outlined in Section 6.4 executes. Sales can demo the platform to prospects with the messaging: "we support FourKites today, project44 in two weeks, your vendor of choice in a month."

---

## 8. Open items

Things we know we don't yet have answers for. Each should be resolved before the deliverable that depends on it.

| ID | Item | Resolution needed before | Owner |
|---|---|---|---|
| O-001 | FourKites' inbound webhook auth scheme (apikey? Basic? HMAC?) | Deliverable #5 (webhook controller) | Glen confirms with FourKites CSM |
| O-002 | TruckTools status code → EDI 214 mapping table | Deliverable #4 (insertion points) | Glen pulls from audit log |
| O-003 | OTR API .NET 4.8.1 upgrade test plan | Deliverable #3 (upgrade playbook) | Designed by Claude, executed by Glen |
| O-004 | VB.NET version of the consuming app | Deliverable #6 (POD sample) | Glen confirms |
| O-005 | Whether to retain `FourKitesIntegration.OutboundService` or formally retire it | Phase 1 wrap-up | Glen's call |
| O-006 | Whether to retain `FourKitesIntegration.WebhookReceiver` (likely no — replaced by OTR API controller) | Phase 1 wrap-up | Glen's call |
| O-007 | API key storage — config file, credential vault, encrypted column in DB? | Deliverable #7 (SQL schema) | Glen's call |
| O-008 | Per-shipper API key vs single tenant key | Phase 2 design | Glen confirms FourKites contract model |
| O-009 | Monitoring/alerting platform (Glen's shop standard) | Deliverable #8 | Glen specifies |
| O-010 | OTR API public URL for FourKites callbacks (current URL? new subdomain?) | Deliverable #5 | Glen specifies |

---

## 9. Glossary

| Term | Meaning |
|---|---|
| **OTR API** | The existing Vector webservice at `C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\OTR API\`. Bridges Vector FBS and TruckTools. The hub for Phase 1. |
| **FBS / Vector FBS** | Vector's Freight Broker System. The TMS. Source of "matched trips" sent to OTR API. Phase 2 caller. |
| **TruckTools (TT)** | Third-party load tracking service. Sends status/location webhooks to OTR API. The data source for Phase 1. |
| **FourKites (FK)** | The first vendor. Supply chain visibility platform. API destination. |
| **VectorLoadId** | The universal correlation key. Every record carries this. |
| **TruckToolsLoadId** | Secondary identifier; used internally by TT. |
| **BillToCode** | FourKites-specific shipper identifier. From FourKites Connect setup. |
| **Dispatcher Update** | FourKites' single endpoint that handles location, event, stop, assignment, ETA, temperature, load-info updates. The workhorse. |
| **EDI 214** | The Transportation Carrier Shipment Status Message. Standard EDI for shipment status. FourKites uses its status codes (X1, AF, D1, X3, etc.) as the de-facto API codes too. |
| **POD** | Proof of Delivery. Document scanned/photographed at delivery. Uploaded to FourKites as document type "DR". |
| **HMAC auth** | OTR API's existing authentication scheme for incoming requests from Vector/TT. Will NOT be the scheme for FourKites callbacks. |
| **ClientProfile** | Per-shipper, per-vendor configuration row. The framework's routing brain. |
| **Phase 0 / 1 / 2 / 3** | See Section 2.3. |
| **`Vendor.Common`** | Framework DLL. Lives at the heart of everything. |
| **`Vendor.FourKites`** | FourKites-specific DLL. References `Vendor.Common`. The reference implementation. |
| **`VendorAPI_FK`** | The new database. Houses outbound transactions, inbound callbacks, ClientProfiles, cross-references. |

---

## 10. Document inventory

Where this document fits among the others.

| # | Title | File | Status |
|---|---|---|---|
| 1 | Master Strategy (this doc) | `_deliverables\01_Strategy\01_Master_Strategy.md` | ✅ This file |
| 2 | Refactor plan: FourKitesIntegration → Vendor.FourKites | `_deliverables\02_Refactor\` | Pending |
| 3 | OTR API .NET 4.8.1 upgrade playbook | `_deliverables\03_OTR_Upgrade\` | Pending |
| **10** | **Vendor.Common framework design (PROMOTED — see D-025)** | `_deliverables\10_Framework_Design\` | **Pending — build first** |
| 4 | Phase 1 — OTR API integration insertion points (rewrite per #10) | `_deliverables\04_OTR_Insertion_Points\` | Pending rewrite |
| 5 | Phase 1 — FourKites webhook receiver controller | `_deliverables\05_Webhook_Receiver\` | Pending |
| 6 | Phase 1 — VB.NET POD upload sample | `_deliverables\06_VB_POD\` | Pending |
| 7 | Phase 1 — VendorAPI_FK SQL schema | `_deliverables\07_SQL_Schema\` | Pending |
| 8 | Phase 1 — 95% success rate dashboard | `_deliverables\08_Dashboard\` | Pending |
| 9 | Phase 2 — FBS / EDI replacement design | `_deliverables\09_Phase2_FBS\` | Pending |
| 11 | Master handoff package | `_deliverables\11_Handoff\` | Pending |

---

## 11. Worklog

A separate file `_deliverables\_worklog.md` tracks what was produced when, for billing purposes.

---

*End of Master Strategy document.*
