# FourKites Integration Platform — Master Handoff

**Document:** Deliverable #11 of 11 — Master Handoff Package
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson
**Engagement period:** Multiple sessions; final session produced 10 deliverables in one pass

> **Read this first.** This is the entry point to the engagement output. Everything else is referenced from here. If you're new to this project, spend 10 minutes here before opening any other document.

---

## 0. What this engagement produced

A **framework-first vendor integration platform** that lets Vector dispatch business events to FourKites (today) and any future vendor (tomorrow) without changing caller code. Currently sized for Vector's own use but **designed with explicit resale intent** — the framework can be sold to other brokers/shippers as a multi-vendor integration platform.

### One-paragraph summary

OTR API, FBS, and a VB.NET POD application all dispatch business events (load created, location reported, document available, etc.) by calling one method: `VendorDispatcher.Instance.Dispatch(event)`. The dispatcher looks up which vendors each shipper is configured for, hands the event to the right vendor adapter, and records every attempt in a single audit database. FourKites is the first vendor; adding a second (project44, Macropoint, anything) requires writing one new adapter class plus a config row — **zero changes to OTR API, FBS, or VB.NET code**.

### What was built

| Layer | What exists |
|---|---|
| **Framework** | `Vendor.Common` — vendor-agnostic events, dispatcher, adapter contract, configuration loader, audit persistence, webhook correlation |
| **FourKites adapter** | `Vendor.FourKites` — implements the framework for FK specifically (EDI 214 mapping, payload translation, auth, rate limiting, webhook parsing, FK-specific side effects) |
| **Outbound integration (OTR API)** | 5 insertion points across `TruckerToolsController` and `TruckerToolsTrackingController` that dispatch events |
| **Inbound integration (OTR API)** | New `VendorWebhookController` + background `WebhookCorrelator` to receive and correlate FK callbacks |
| **VB.NET POD upload** | `PodUploader` class showing desktop/VB.NET integration |
| **Database** | `VendorAPI_FK` on `10.10.9.10\SQLEXPRESS12` — 5 tables, 4 stored procedures, idempotent migration scripts |
| **Monitoring** | 11 SQL queries powering a 95% SLA dashboard with drill-down, alerting, monthly compliance reporting |
| **Operational playbooks** | Refactor plan, OTR API .NET upgrade plan, Phase 2 design |

### What was NOT built (deliberately)

See Section 9. Briefly: this engagement is **design and integration architecture**, not application implementation. Most code lives as samples / patterns / merge guides that Glen executes against the actual source trees. The exception is the SQL schema (`.sql` files are runnable as-is).

---

## 1. Document inventory

All deliverables live under `_deliverables\` in this TFS project folder.

| # | Path | One-line description |
|---|---|---|
| 1 | `01_Strategy\01_Master_Strategy.md` | Master strategy with 25-entry decision log, 8 architectural principles, phased roadmap, glossary |
| 2 | `02_Refactor\02_Refactor_Plan.md` | Step-by-step plan to refactor `FourKitesIntegration` solution into `Vendor.Common` + `Vendor.FourKites` |
| 3 | `03_OTR_Upgrade\03_OTR_Upgrade_Playbook.md` | OTR API .NET 4.6.1 → 4.8.1 upgrade with rollback procedure |
| 4 (v1) | `04_OTR_Insertion_Points\04_OTR_Insertion_Points.md` | **SUPERSEDED** — original FK-direct version, kept for historical contrast |
| 4 (v2) | `04_OTR_Insertion_Points\04_OTR_Insertion_Points_v2.md` | Framework-first OTR API insertion points — 5 endpoints calling VendorDispatcher |
| 5 | `05_Webhook_Receiver\05_Webhook_Receiver.md` | Inbound webhook handling — VendorWebhookController + FourKitesWebhookProcessor |
| 6 | `06_VB_POD\06_VB_POD.md` | VB.NET POD upload integration |
| 7 | `07_SQL_Schema\07_SQL_Schema.md` + `scripts\*.sql` | VendorAPI_FK schema with 9 runnable SQL scripts |
| 8 | `08_Dashboard\08_Dashboard.md` + `queries\*.sql` | 95% SLA dashboard design + 11 query files |
| 9 | `09_Phase2_FBS\09_Phase2_FBS.md` | Phase 2 FBS / EDI replacement design (design-only) |
| 10 | `10_Framework_Design\10_Framework_Design.md` | The Vendor.Common framework itself — events, adapter contract, dispatcher |
| 11 | `00_README.md` | This document |
| — | `_worklog.md` | Billing worklog — what was done, in what phases, for invoicing |

**Note on numbering:** Deliverable #10 was **promoted ahead of #4** during the engagement (decision D-025). The numbers reflect the original plan; the build order is documented in Section 3 below.

---

## 2. Where to start, depending on who you are

### "I'm Glen, picking this up next week"

1. Skim `_worklog.md` to recall what was billed
2. Re-read `01_Strategy\01_Master_Strategy.md` Section 3 (the decision log) — that's the highest-density refresher
3. Open the open-items roll-up in Section 7 of this README — that's what needs your input to unblock work

### "I'm a new engineer being onboarded"

1. Read **this document** end-to-end (15 minutes)
2. Read `01_Strategy\01_Master_Strategy.md` (30 minutes; skim later sections)
3. Read `10_Framework_Design\10_Framework_Design.md` (20 minutes) — the framework is the heart
4. Skim `04_OTR_Insertion_Points\04_OTR_Insertion_Points_v2.md` to see how callers use the framework
5. Open the SQL scripts in `07_SQL_Schema\scripts\` and read one or two

After that you should have a working mental model. Other deliverables are detail-on-demand.

### "I'm doing implementation work, where do I start?"

The build order is in Section 3. The short version: refactor first (#2), then schema (#7), then framework + OTR API in parallel (#10, #4-v2, #5), then VB.NET (#6), then dashboard (#8). Phase 2 (#9) waits until Phase 1 is production-stable.

### "I'm leadership / sales, what was built?"

Section 0 above. The vendor framework. Two outputs that matter for sales: (a) Vector has working FK integration on a platform that can scale to multi-vendor; (b) the platform itself is resaleable to other brokers — the design pressure-tested this against a hypothetical second vendor.

---

## 3. How to build this — dependency order

The deliverables interlock. Building things in the wrong order causes rework. Recommended sequence:

```
              ┌──────────────────────┐
              │ Strategy (#1)         │  ← decisions; read first, build nothing
              └──────────┬───────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ Refactor (#2)         │  ← solution → Vendor.Common + Vendor.FourKites
              └──────────┬───────────┘
                         │
                ┌────────┴────────┐
                ▼                 ▼
     ┌────────────────────┐  ┌──────────────────────┐
     │ OTR Upgrade (#3)   │  │ SQL Schema (#7)      │
     │ 4.6.1 → 4.8.1      │  │ Build VendorAPI_FK    │
     └────────┬───────────┘  └──────────┬──────────┘
              │                          │
              ▼                          ▼
          (both must complete before next stage)
                         │
                         ▼
              ┌──────────────────────┐
              │ Framework (#10)       │  ← the Vendor.Common code itself
              │ + FourKites adapter   │
              └──────────┬───────────┘
                         │
              ┌──────────┼──────────┐
              ▼          ▼          ▼
   ┌──────────────┐ ┌──────────┐ ┌────────────┐
   │ OTR Insert   │ │ Webhook  │ │ VB.NET POD │
   │ Points (#4)  │ │ Recv (#5)│ │ (#6)       │
   └──────┬───────┘ └────┬─────┘ └─────┬──────┘
          └──────────────┼─────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ Dashboard (#8)        │  ← wires queries into BI tool
              └──────────┬───────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ PHASE 1 PRODUCTION    │  ← stabilize for 30+ days
              └──────────┬───────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ Phase 2 FBS (#9)      │  ← only after Phase 1 stable
              └──────────────────────┘
```

**Total estimated implementation effort (engineer-hours, rough):**

| Stage | Effort |
|---|---|
| Refactor + OTR Upgrade | 8-12 hours |
| SQL schema deployment | 1-2 hours |
| Framework + FK adapter code | 20-30 hours |
| OTR API integration (insert points + webhooks) | 10-15 hours |
| VB.NET POD | 4-6 hours |
| Dashboard wire-up | 4-8 hours (depends on BI tool) |
| **Phase 1 total** | **~50-75 engineer-hours** |
| Phase 2 (FBS) | 60-100 hours over 6 months calendar (mostly stability windows) |

These are estimates. Actual hours depend on Vector-specific factors Glen knows better than I do (codebase familiarity, test environment availability, deployment friction).

---

## 4. Architecture in one diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│  CALLERS                                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────────┐  ┌──────────────┐    │
│  │ OTR API    │  │ VB.NET POD │  │ Vector FBS     │  │ Future       │    │
│  │ (outbound) │  │ App        │  │ (Phase 2)      │  │ caller       │    │
│  └─────┬──────┘  └─────┬──────┘  └────────┬───────┘  └──────┬───────┘    │
│        │               │                  │                  │             │
│        └───────────────┴──────────────────┴──────────────────┘             │
│                            │                                                │
│        VendorDispatcher.Instance.Dispatch(event)                            │
│                            │                                                │
├────────────────────────────┼────────────────────────────────────────────────┤
│  FRAMEWORK (Vendor.Common.dll)                                              │
│                            │                                                │
│   ┌─── Vendor-agnostic events ──┐                                          │
│   │  LoadCreatedEvent             │     ┌─────────────────────────────┐    │
│   │  LoadAssignedEvent            │     │  ClientProfiles (DB)         │    │
│   │  LocationReportedEvent        │     │  per-shipper, per-vendor     │    │
│   │  LoadStatusEvent              │ ◀───│  routing + config           │    │
│   │  LoadTrackingStoppedEvent     │     └─────────────────────────────┘    │
│   │  DocumentAvailableEvent       │                                        │
│   └───────────────┬───────────────┘                                        │
│                   │                                                         │
│                   ▼                                                         │
│   ┌───── VendorDispatcher ──────┐                                          │
│   │  - resolves shipper           │     ┌─────────────────────────────┐    │
│   │  - looks up routing           │ ───▶│ VendorOutboundTransactions  │    │
│   │  - calls adapter              │     │  (audit log for every        │    │
│   │  - records audit              │     │   dispatch attempt)          │    │
│   └───────────────┬───────────────┘     └─────────────────────────────┘    │
│                   │                                                         │
├───────────────────┼─────────────────────────────────────────────────────────┤
│  VENDOR ADAPTERS  │                                                         │
│                   │                                                         │
│   ┌───────────────▼────────────┐  ┌────────────────────┐                   │
│   │ Vendor.FourKites           │  │ Vendor.Project44   │                   │
│   │  FourKitesAdapter          │  │  (future)          │                   │
│   │  - translates events       │  │                    │                   │
│   │  - calls FourKitesClient   │  │                    │                   │
│   │  - rate limits             │  │                    │                   │
│   └───────────────┬────────────┘  └────────────────────┘                   │
│                   │                                                         │
└───────────────────┼─────────────────────────────────────────────────────────┘
                    │
                    ▼
              ┌─────────────┐
              │  FourKites  │
              │  REST API   │
              └─────┬───────┘
                    │
                    │  (inbound webhooks)
                    ▼
┌────────────────────────────────────────────────────────────────────────────┐
│  INBOUND PATH                                                                │
│  ┌────────────────────────────────────┐                                     │
│  │ OTR API: VendorWebhookController    │                                     │
│  │  POST /api/vendorwebhook/{vendor}   │                                     │
│  │  - persists to VendorInboundCallbacks                                     │
│  └─────────────────┬───────────────────┘                                     │
│                    │                                                          │
│                    ▼                                                          │
│  ┌────────────────────────────────────┐                                     │
│  │ WebhookCorrelator (background)      │                                     │
│  │  - matches callbacks to outbound tx │                                     │
│  │  - calls adapter.OnConfirmedAsync   │                                     │
│  └────────────────────────────────────┘                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. The 25 decisions, at a glance

Full text of each decision is in `01_Strategy\01_Master_Strategy.md` Section 3. This is just the index so you know what's there.

| ID | Decision |
|---|---|
| D-001 | OTR API receives webhooks (vs. standalone service) |
| D-002 | Phase 2 deferred; Phase 1 ships single-vendor |
| D-003 | Single repo with multiple projects (not microservices) |
| D-004 | Built-in `HttpClientFactory` pattern; no DI framework |
| D-005 | OTR API references DLL directly (no HTTP middleman) |
| D-006 | Polly for retries (3 attempts, exponential backoff) |
| D-007 | Newtonsoft.Json 13.0.3 (not System.Text.Json — VB.NET compat) |
| D-008 | VectorLoadId is the universal correlation key |
| D-009 | New DB `VendorAPI_FK` on existing SQL Express server |
| D-010 | Webhook receiver in OTR API (not standalone) |
| D-011 | 95% SLA monitoring is first-class |
| D-012 | EDI 214 status mapping in `Vendor.FourKites`, not OTR API |
| D-013 | Upgrade OTR API 4.6.1 → 4.8.1 (Glen accepts risk) |
| D-014 | Only Glen edits OTR API source; Claude provides merge guides |
| D-015 | VB.NET app references DLL directly (no OTR hop) |
| D-016 | TT status code → EDI 214 mapping derived from audit log |
| D-017 | Stored procedures only where they materially help |
| D-018 | Rename `FourKitesIntegration.Core` → `Vendor.FourKites`; `VendorName` column day one |
| D-019 | Phase 2 FBS references DLL directly; old OutboundService and WebhookReceiver Windows Services retired |
| D-020 | Framework-first build (resale intent reverses Rule-of-Three) |
| D-021 | API/EDI switching logic lives in FBS, not the DLL |
| D-022 | All-phases-in-this-engagement (prevent context loss) |
| D-023 | Worklog maintained per session for billing |
| D-024 | One DLL per vendor (not split by direction/audience) |
| D-025 | **Course correction:** promoted Framework Design (#10) ahead of OTR Insertion Points (#4) |
| D-026 | Database name `VendorAPI_FK` — FK officially means **FrameworK**, not FourKites |

D-025 is the one course correction in the engagement. Claude originally wrote Deliverable #4 in a FourKites-direct style; Glen caught it with one question ("which part is framework?"); rewrote #4 framework-first after building #10 first. Documented openly so future readers understand the contrast between the superseded v1 and the v2.

---

## 6. Glossary

Terms you'll see across the deliverables.

| Term | Meaning |
|---|---|
| **Adapter** | Vendor-specific class implementing `IVendorAdapter`. Translates framework events to vendor API calls. One per vendor (e.g., `FourKitesAdapter`). |
| **ACK** | Vendor returned HTTP 2xx synchronously. The FK SLA metric. |
| **Callback** | An inbound webhook from a vendor. Stored in `VendorInboundCallbacks`. |
| **ClientProfile** | DB row defining "for shipper X, route these events to vendor Y with this config." |
| **CONFIRMED** | Vendor's webhook confirmed the event was processed (stricter than ACK). |
| **Correlator** | Background worker that matches inbound callbacks to outbound transactions. |
| **Dispatcher** | `VendorDispatcher` — the single API callers use. Routes events to adapters. |
| **EDI 214** | Electronic Data Interchange shipment status message. FK uses it as one of multiple status formats. |
| **Event** | Vendor-agnostic POCO describing something that happened (e.g., `LoadAssignedEvent`). |
| **FBS** | Vector's Freight Brokerage System — internal load management system. Phase 2 target. |
| **FK (in `VendorAPI_FK`)** | **Officially means "FrameworK", not FourKites** (per D-026). The database is vendor-agnostic and does not need renaming when adding vendor #2. |
| **Framework** | The vendor-agnostic layer (`Vendor.Common`) that all callers and adapters share. |
| **Insertion point** | A specific place in caller code where a dispatcher call is added. |
| **OTR API** | Vector's existing webservice that bridges Vector FBS ↔ TruckTools. Phase 1 caller. |
| **ShipperCode** | Identifier for a shipper. `VECTOR_DEFAULT` is the Phase 1 catch-all. |
| **SLA** | The 95% success rate FK contractually requires. |
| **Switchboard** | FBS-internal routing brain (Phase 2) that decides API vs EDI per shipper-event. |
| **Transaction** | A row in `VendorOutboundTransactions` representing one outbound dispatch attempt. |
| **TruckTools** | Third-party tracking platform; one of OTR API's existing integrations. |
| **VectorLoadId** | Vector's internal identifier for a load. The universal correlation key. |
| **VendorLoadId** | The vendor's internal identifier for a load (e.g., FourKitesLoadId). |

---

## 7. Open items roll-up

Across all deliverables, here are the unresolved items, grouped by who needs to act.

### Glen confirms with FourKites CSM

| ID | Item |
|---|---|
| O-001 | Inbound webhook auth scheme: apikey, basic, or IP allowlist only |
| O-801 | Whether contractual SLA metric is ACK rate or CONFIRMED rate |

### Glen confirms internally (FBS / Vector knowledge)

| ID | Item |
|---|---|
| O-101 | Investigate origin of `Settings.dll` in OTR API packages folder (unknown source) |
| O-202 | Verify property names on OTR's `Load` model (truckNumber, trailerNumber, driverName, driverCell, carrierSCAC, etc.) |
| O-203 | Confirm TT timestamp format edge cases |
| O-401 | Stop/reference mapping richness for `LoadCreatedEvent` |
| O-402 | Shipper code source on the Load model (for future multi-shipper) |
| O-501 | Where Vector's Load table lives + its column names (`dbo.[Load]` assumed; confirm) |
| O-601 | Which specific VB.NET app is the POD uploader |
| O-602 | VB.NET app integration scenario (own DB / lookup / file watcher) |
| O-603 | Existing logger in VB.NET app (log4net, NLog, etc.) |
| O-701 | Option A vs B for vendor load IDs (table additions vs cross-reference table) |
| O-901 | FBS language and trigger architecture |
| O-902 | Catalog of existing FBS EDI messages by shipper |
| O-907 | FBS's outbound EDI generation code — is it switchboard-friendly today? |
| O-908 | Are any shipper contracts EDI-mandatory? |

### Operational decisions for production deployment

| ID | Item |
|---|---|
| O-007 | Production API key storage (encryption at rest, DPAPI, vault?) |
| O-010 | Production OTR API public hostname for FK webhook URL |
| O-502 | Encryption-at-rest for `ConfigJson` credentials |
| O-702 | Retention policy for `VendorOutboundTransactions` (recommend 90 days) |
| O-703 | Encryption strategy for `ConfigJson` |
| O-704 | SQL grants for VectorFBS's identity (Phase 2) |
| O-705 | Backup/restore policy for `VendorAPI_FK` |
| O-802 | BI tool choice for dashboard |
| O-803 | Alerting channel (PagerDuty, email, etc.) |
| O-804 | Dashboard internet-accessible vs intranet |
| O-805 | Dashboard snapshot retention |

### Smaller / lower-priority

| ID | Item |
|---|---|
| O-002 | Populate TT status code → EDI 214 mapping from real audit data |
| O-204 | Production API key delivery mechanism |
| O-205 | Whether FK requires IP allowlisting OTR API |
| O-503 | Adapter eager vs lazy loading |
| O-504 | Where `vendorAdapters` config section lives |
| O-505 | ClientProfile cache TTL |
| O-604 | VB.NET deployment mechanism |
| O-605 | Should VB.NET app keep talking to OTR API for other operations? |
| O-903 | Which shipper is the Phase 2 pilot |
| O-904 | Per-shipper contract language on API vs EDI |
| O-905 | New `SourceSystem` value for FBS dispatches |
| O-906 | When to add `AppointmentScheduledEvent` / `AppointmentRescheduledEvent` |

**Roughly 40 open items.** That sounds like a lot, but most are small confirmations (table names, deployment specifics, operational policy choices). The actual blockers to starting Phase 1 build are concentrated in the first group (FK CSM confirmation) and the second group (Vector internals Glen can answer).

---

## 8. Engagement metrics

| Metric | Value |
|---|---|
| Deliverables completed | 11 (one was rewritten — both versions retained) |
| Total documentation | ~6,000 lines of markdown |
| SQL files (runnable) | 20 (9 schema + 11 dashboard queries) |
| Decisions formally captured | 26 (D-001 through D-026) |
| Open items identified | ~40 |
| Code samples shown | ~1,500 lines (C# and VB.NET, all illustrative — Glen implements against real source) |
| Course corrections logged | 1 (D-025, with both v1 and v2 of Deliverable #4 retained for contrast) |
| Engagement style | Iterative, decision-tracked, hand-execution model (Glen edits source, Claude provides merge guides) |

---

## 9. What this engagement deliberately did NOT do

Saying no is design too. Things we considered and skipped, with reasons:

| Skipped | Why |
|---|---|
| Implementing the actual code (writing files into OTR API, FBS, VB.NET app source trees) | D-014: Glen executes; Claude designs. Hand-execution model. |
| Building a DI framework or IoC container | D-004: Constructor injection + factory pattern is sufficient at this scale. |
| Per-vendor circuit breakers | Polly's per-call retries cover Phase 1; circuit breaking is Phase 3. |
| Cross-vendor rate limit coordination | Each vendor's limit is independent; coordination doesn't help. |
| Event sourcing / persisting events before dispatch | Audit log captures request payload — sufficient for replay. |
| Dead-letter queue with auto-retry | Failed transactions stay queryable; manual retry is fine for now. |
| Multi-tenancy (tenant isolation in framework) | Vector is one tenant; resale customers are their own tenants in their own deployments. |
| Per-event-type fire-and-forget configuration | Single global flag is simpler; revisit if needed. |
| Mocking infrastructure for adapter testing | Glen confirms test approach when implementation starts. |
| Performance load testing plan | Out of scope for design engagement. |
| Production deployment automation (CI/CD pipelines) | Out of scope; uses Vector's existing deployment processes. |

If a future engineer asks "why didn't we do X?" — check this list first. Most reasonable additions to the platform were considered.

---

## 10. The two grep tests that prove the framework holds

A future engineer maintaining this code can verify the framework abstraction is intact with two commands:

```bash
# In OTR API source (after Deliverable #4-v2 is merged):
grep -ri "fourkites" .
# Expected result: ZERO matches except in .csproj HintPath lines

# In VB.NET POD app source (after Deliverable #6 is implemented):
grep -ri "fourkites" .
# Expected result: ZERO matches except in App.config's <vendorAdapters> registration
```

If either grep returns matches in actual code, the framework boundary has been violated and we've drifted back toward vendor-direct coupling. This is the smoke test that should run in CI eventually.

---

## 11. What "done" looks like

This engagement is done. The build is not.

**This engagement (design + handoff)** is complete when:
- [x] All 11 deliverables exist in `_deliverables\`
- [x] Worklog captures phases A through M for billing
- [x] Master strategy includes all 25 decisions
- [x] Open items are rolled up in this README
- [x] Glen acknowledges receipt

**The Phase 1 implementation** is complete when:
- [ ] Refactor (#2) executed: `Vendor.Common` and `Vendor.FourKites` exist as built DLLs
- [ ] OTR API upgraded to .NET 4.8.1 (#3)
- [ ] `VendorAPI_FK` database deployed (#7)
- [ ] Framework code written (#10's design implemented)
- [ ] OTR API insertion points merged (#4-v2)
- [ ] Webhook receiver running (#5)
- [ ] VB.NET POD upload working (#6)
- [ ] Dashboard live (#8)
- [ ] 30 days of clean production operation at ≥95% SLA

**Phase 2** starts after Phase 1's 30-day stability window.

---

## 12. Acknowledgments and notes

This engagement was conducted as a multi-session design conversation. The output is the design itself, not the implementation. The honest summary of what made it work:

- Glen brought domain knowledge (Vector's stack, FBS, OTR API history, FK contractual context, freight brokerage operational realities)
- Claude brought patterns (framework design, event modeling, audit schema, dashboard structure) and the discipline of writing it down decision-by-decision
- The course correction (D-025) is documented because it's the most useful thing to be transparent about — what looked like the right direction (FK-direct, Rule-of-Three) wasn't, given resale intent (D-020). Glen caught the drift, the rewrite was clean

The framework abstraction is intentionally narrow: vendor-agnostic events + one adapter per vendor + one audit log. Wider abstractions were considered (event sourcing, cross-vendor coordination, etc.) and explicitly rejected for Phase 1. If they're needed later, they're additive.

---

## 13. Quick reference card

A pocket version of the things you'll look up often.

**DB connection string:**
```
Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True
```

**The one method callers invoke:**
```csharp
VendorDispatcher.Instance.Dispatch(new SomeEvent { VectorLoadId = "...", ... });
```

**The seven framework event types:**
- `LoadCreatedEvent`
- `LoadAssignedEvent`
- `LocationReportedEvent`
- `LoadStatusEvent`
- `LoadTrackingStoppedEvent`
- `DocumentAvailableEvent`
- `GenericLoadEvent`

**The webhook URL given to FK CSM:**
```
POST https://<otr-api-public-host>/api/vendorwebhook/fourkites
```

**The query that answers "what happened with load X?":**
```sql
EXEC dbo.usp_GetLoadAuditTrail @VectorLoadId = 'LOAD12345';
```

**The query that answers "are we meeting SLA right now?":**
```sql
-- See: _deliverables\08_Dashboard\queries\01_headline_success_rate.sql
```

**The five OTR API insertion points:**
| Endpoint | Event |
|---|---|
| `PostLoad` | `LoadCreatedEvent` |
| `TrackLoad` | `LoadAssignedEvent` |
| `UpdateTrackLoad` | `LoadAssignedEvent` |
| `SendStatus` | `LocationReportedEvent` + `LoadStatusEvent` |
| `CancelLoadTracking` | `LoadTrackingStoppedEvent` |

**The success-rate color thresholds:**
- Green: ≥98%
- Yellow: 95-97.9%
- Orange: 90-94.9%
- Red: <90%

---

*End of Master Handoff. End of engagement output.*
