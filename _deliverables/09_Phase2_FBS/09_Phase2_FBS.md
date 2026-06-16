# Phase 2 — Vector FBS / EDI Replacement Design

**Document:** Deliverable #9 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (designer)
**Prerequisites:** Phase 1 (Deliverables #1-8, #10) in production and stable for at least 30 days
**Related decisions:** D-002, D-019, D-021, D-024
**Status:** Design-only. No code or schema. Implementation deferred to after Phase 1 stabilizes.

---

## 0. Purpose

This deliverable designs **Phase 2** of the platform: Vector FBS calls the vendor framework directly to selectively replace EDI traffic that goes to FourKites today.

After Phase 2:
- FBS references `Vendor.Common.dll` and `Vendor.FourKites.dll` directly (no OTR API hop)
- FBS dispatches business events (rate confirms, pickup changes, etc.) through `VendorDispatcher` exactly like OTR API does
- For each event + shipper combination, FBS decides: **API, EDI, both, or neither** — a per-row config decision, not a code change
- **No duplicate sends:** if API succeeds, EDI doesn't fire for the same event; if EDI fires, API doesn't
- The full audit story (every load, every event, every channel) is queryable from `VendorAPI_FK.VendorOutboundTransactions`

**Why this is design-only:** Phase 2 is too far from Phase 1's current implementation to commit to specifics. Glen needs to confirm:
- FBS's language (VB.NET, C#, or mixed)
- FBS's existing event/trigger architecture
- Which EDI partners and messages are candidates for cutover
- The contractual timing — when shippers can be moved off EDI

This document gives the **pattern** and **decision framework** that holds regardless of those specifics. When Phase 2 starts, this becomes the foundation for the detailed implementation plan.

---

## 1. The big picture

```
                  Today (Phase 1 complete, Phase 2 not started):

      ┌──────────────┐                       ┌──────────────┐
      │  Vector FBS  │─── EDI 214 / 990 ────▶│  Trading     │
      │              │   (current channel)   │  Partners +  │
      │              │                       │  FourKites   │
      │              │                       └──────────────┘
      │              │
      │              │── load posts ─────────▶ OTR API ──▶ TruckTools
      └──────────────┘                       (Phase 1)

                              ▼

                  After Phase 2:

      ┌──────────────────────────────────┐
      │  Vector FBS                       │
      │                                   │
      │   Business event happens          │
      │   (e.g., Rate Confirmed)          │
      │           │                       │
      │           ▼                       │
      │   ┌─────────────────────────┐    │
      │   │  FBS Switchboard         │    │
      │   │  "For this shipper +     │    │
      │   │   event, what channels?" │    │
      │   └────┬───────────────┬─────┘    │
      │        │               │           │
      │     API│               │EDI       │
      │        ▼               ▼           │
      │   ┌─────────┐    ┌──────────┐    │
      │   │ Vendor  │    │  Existing │    │
      │   │ Disp.   │    │  EDI gen  │    │
      │   └────┬────┘    └─────┬─────┘    │
      └────────┼───────────────┼─────────┘
               │               │
               ▼               ▼
         FourKites        Trading
         (Phase 1)        Partners (incl.
         framework        FK if not cut over)
```

**The crucial new component is the switchboard.** It's FBS-internal, not framework code. The framework knows nothing about EDI.

---

## 2. What FBS dispatches — the event catalog

These are plausible FBS-originated events. Glen confirms the actual list during Phase 2 kickoff; this is the **shape** of the catalog, not a contract.

| Event type | Trigger in FBS | Equivalent EDI | Framework event |
|---|---|---|---|
| Load tendered to carrier | Brokerage offers load; carrier accepts | EDI 204 outbound | `LoadCreatedEvent` (if not already created via OTR) — usually skipped because OTR handles it |
| Rate confirmed | Carrier signs rate confirm | (no standard EDI; FK proprietary status) | `LoadStatusEvent` with status `Dispatched` + raw "RATE_CONFIRMED" |
| Pickup appointment scheduled / rescheduled | Dispatcher books or moves a pickup | EDI 990 (response to 204) | `LoadStatusEvent` or new `StopAppointmentChangedEvent` |
| Delivery appointment scheduled / rescheduled | Same, delivery side | EDI 990 | Same |
| Carrier reassigned | Original carrier reneges, new one assigned | EDI 990 cancel + new 204 | `LoadAssignedEvent` (new carrier info) |
| Load cancelled / dispatcher-stopped | Shipper or broker cancels the load | EDI 990 cancel | `LoadTrackingStoppedEvent` with reason `CANCELLED` |
| POD received | Carrier submits proof of delivery via portal | (out of band; email or upload) | `DocumentAvailableEvent` — overlaps with VB.NET POD app from #6 |
| Invoice generated | Accounting milestone; visibility-relevant for some shippers | EDI 210 | Probably skip; not visibility-relevant |
| Status milestone (in-transit, arrived, etc.) | Computed from check-call data | EDI 214 | `LoadStatusEvent` |

**Two important observations about this list:**

1. **Several events overlap with OTR API's dispatches** (TruckerTools-driven location/status events). When the same logical event has two possible sources, we have to decide which source authoritatively dispatches. Section 6 covers the deduplication strategy.

2. **The "Rate Confirmed" event** doesn't have a clean EDI 214 equivalent — FK uses it as a milestone for some shippers, others ignore it. This is a candidate for API-only delivery from day one (no EDI to displace).

---

## 3. The switchboard — FBS's per-event routing brain

The switchboard is **FBS-internal code**. It is not part of `Vendor.Common`. The framework doesn't know about EDI; FBS shouldn't push EDI concerns down into the framework.

### 3.1 What it does

For each (event, shipper) combination, the switchboard answers four questions:

1. **Should we dispatch at all?** (Some shippers don't subscribe to FourKites visibility at all.)
2. **Via API?** (Is FK + this event a vendor we're sending to today?)
3. **Via EDI?** (Is there an active EDI trading partner relationship for this event + shipper?)
4. **What's the EDI cutover state?** (Is this shipper still on EDI, on API, or in dual-write parallel-run?)

### 3.2 Where the rules live

A SQL table in Vector's FBS database — or a config file, depending on Vector's conventions:

```
FBS_ChannelRouting (proposed shape — Glen confirms during Phase 2)
├── RoutingId           bigint identity
├── ShipperCode         nvarchar(50)
├── EventType           nvarchar(100)    -- "RateConfirmed", "AppointmentChanged", etc.
├── ApiEnabled          bit
├── EdiEnabled          bit
├── EdiPartnerId        nvarchar(50) null   -- which EDI trading partner gets this
├── EffectiveStartUtc   datetime2
├── EffectiveEndUtc     datetime2 null      -- null = current
├── Notes               nvarchar(500)
└── unique(ShipperCode, EventType, EffectiveStartUtc)
```

**Why this shape:**
- Single table answers all four switchboard questions
- The effective-date columns allow scheduled cutovers (insert a row with `EffectiveStartUtc = 2026-08-01` to cut a shipper over)
- The unique constraint prevents conflicting overlapping rules
- `EdiPartnerId` is nullable because some events don't have EDI at all (e.g., POD)
- Easy to query for the dashboard ("show me which shippers are on API for which events")

### 3.3 The four routing outcomes

For a given (Shipper, Event):

| ApiEnabled | EdiEnabled | Outcome | When used |
|---|---|---|---|
| true | false | **API only** | Target state for most shippers after cutover |
| false | true | **EDI only** | Pre-cutover state; or shippers that refuse to leave EDI |
| true | true | **Dual-write (parallel-run)** | Cutover validation window — verify API works before turning EDI off |
| false | false | **Neither** | Event is suppressed for this shipper (rare; usually means the shipper doesn't want this event at all) |

The dual-write case is the most operationally complex and most important — Section 5 covers it.

### 3.4 Switchboard call site

In FBS code, every business event flows through one method:

```
' Pseudo-VB.NET (Glen confirms actual FBS language and style)
Sub OnRateConfirmed(load)
    Dim routing = FbsSwitchboard.GetRouting(load.ShipperCode, "RateConfirmed")

    If routing.ApiEnabled Then
        VendorDispatcher.Instance.Dispatch(BuildRateConfirmedEvent(load))
    End If

    If routing.EdiEnabled Then
        ExistingEdiGenerator.SendRateConfirmation(load, routing.EdiPartnerId)
    End If

    ' Audit the routing decision itself
    AuditLog.RecordRoutingDecision(load.ShipperCode, "RateConfirmed", routing)
End Sub
```

Two important properties:
- **Independent branches.** Both checks run independently. Dual-write is just "both branches taken."
- **Audit-the-decision.** Record what routing said, not just what dispatched. Tomorrow's question is "why did this load get EDI when I thought we cut shipper X over?" — answered by the audit row.

---

## 4. Mapping FBS events to framework events

The framework already has 7 event types (from Deliverable #10). Most FBS events map cleanly. Where they don't, we have two options:

**Option α — Use `GenericLoadEvent`** with `EventName = "RateConfirmed"` and the relevant data in the `Data` dictionary. Adapter writers know to look for this. Pro: no framework changes. Con: less type safety, harder for adapters to opt in/out.

**Option β — Add a new event type** to `Vendor.Common.Events`. Adapters that care implement `CanHandle` for it; adapters that don't ignore it. Pro: explicit, type-safe. Con: framework change required.

**Recommendation:** for Phase 2's likely events, lean on existing types where possible. Specifically:

| FBS event | Framework event | Notes |
|---|---|---|
| Rate Confirmed | `LoadStatusEvent` with `StatusType = Dispatched`, `SourceStatusCode = "RATE_CONFIRMED"` | The "dispatched" semantics fit; raw code preserved for adapters that want finer granularity |
| Pickup/Delivery appointment scheduled | **New: `AppointmentScheduledEvent`** — recommend adding | Doesn't fit existing types cleanly |
| Pickup/Delivery appointment rescheduled | **New: `AppointmentRescheduledEvent`** — recommend adding | Same |
| Carrier reassigned | `LoadAssignedEvent` (existing) | Adapters idempotent on reassignment |
| Load cancelled / stopped | `LoadTrackingStoppedEvent` (existing) | `Reason = "CANCELLED"` or `"DISPATCHER_STOPPED"` |
| POD received | `DocumentAvailableEvent` (existing) | Same shape VB.NET POD app uses |
| Status milestone | `LoadStatusEvent` (existing) | Overlaps with OTR; deduplication required (Section 6) |

**Two new event types** (`AppointmentScheduledEvent`, `AppointmentRescheduledEvent`) are additive to `Vendor.Common.Events`. They don't break anything; adapters that don't implement them just decline via `CanHandle`. Same shape as existing events:

```
public class AppointmentScheduledEvent : VendorEvent
{
    public StopRole StopRole { get; set; }              // Pickup or Delivery
    public int? StopSequence { get; set; }
    public DateTime ScheduledArrivalUtc { get; set; }
    public DateTime? ScheduledDepartureUtc { get; set; }
    public StopInfo AtStop { get; set; }
}

public class AppointmentRescheduledEvent : VendorEvent
{
    public StopRole StopRole { get; set; }
    public int? StopSequence { get; set; }
    public DateTime PreviousScheduledArrivalUtc { get; set; }
    public DateTime NewScheduledArrivalUtc { get; set; }
    public string Reason { get; set; }
    public StopInfo AtStop { get; set; }
}
```

These get added to `Vendor.Common.Events` when Phase 2 development begins. The FK adapter learns to translate them to FK's appointment update payloads. The pressure-test against project44 (would they handle these?) is a yes — appointment changes are a standard concept in any visibility platform.

---

## 5. Migration sequencing — how to cut over without breaking things

The unsafe approach: "flip ApiEnabled=true and EdiEnabled=false for all shippers on Monday morning." Don't do this. There are at least three risks:
- API silently fails for a shipper and FK doesn't see updates we expected to deliver
- A shipper's downstream system depends on the EDI format and rejects API
- The contract review at month-end finds the SLA dropped because cutover bugs surfaced under load

The safe approach has four stages per shipper:

### 5.1 Stage 1 — Shadow mode

**Settings:** `ApiEnabled = true`, `EdiEnabled = true`. Dual-write. EDI is still authoritative; API is observational.

**Goal:** Verify the API path works without operational risk. EDI continues to be the source of truth for the shipper. Operations and customers see no change.

**Duration:** 1-2 weeks minimum. The duration matters less than the variety of events seen — you want to see every event type at least 10 times before you trust it.

**Validation:**
- All API dispatches succeed (≥98% on `VendorAPI_FK.VendorOutboundTransactions`, not just the SLA 95%)
- API and EDI semantics match for the same event (this is the hard part — Section 5.5 covers it)
- No malformed payloads (FK reports no rejections)

### 5.2 Stage 2 — Primary mode

**Settings:** `ApiEnabled = true`, `EdiEnabled = true`. Still dual-write, but operationally API is the primary; EDI is the safety net.

**Goal:** Operationally treat API as the source of truth. If discrepancies surface, the procedure is to fix the API path, not fall back to EDI.

**Duration:** 2-4 weeks.

**Validation:**
- Any discrepancy between API and EDI is investigated within 24 hours
- Operations starts using the dashboard (Deliverable #8) as the primary visibility tool for this shipper, not the EDI logs

### 5.3 Stage 3 — Cutover

**Settings:** `ApiEnabled = true`, `EdiEnabled = false`. API-only.

**Goal:** Stop generating EDI for this shipper-event combination. Reduce VAN costs. Simplify the operational picture.

**Duration:** 4 weeks of close monitoring before declaring the cutover stable.

**Validation:**
- Daily check of dashboard for that shipper for the first 2 weeks
- One-week SLA report shows ≥95% for that shipper's events specifically
- No operational incidents traceable to missing EDI

### 5.4 Stage 4 — Decommission

**Settings:** Unchanged from Stage 3. The cutover row stays in `FBS_ChannelRouting` with `EdiEnabled = false`.

**Goal:** Clean up the EDI infrastructure for this shipper-event. Notify the EDI trading partner (FK or VAN provider) that traffic will drop. Update internal documentation. Retire the EDI generator code path if no other shippers use it.

**Duration:** Whenever convenient, but ideally within 90 days so the EDI code doesn't accumulate.

### 5.5 The hard part — semantic equivalence

Stage 1's "API and EDI semantics match" check is where most cutovers stumble. The API and EDI representations of the same business event are rarely byte-identical. The question is whether they're **functionally equivalent** — does the downstream consumer (FK) treat them as equivalent for their purposes?

**Practical check:** for each event during Stage 1, pull the EDI 214 sent and the API payload sent for the same load+event. Compare:
- Did both arrive at FK within the same minute?
- Did FK reflect the same status / data after both arrived? (Check FK's web UI or their API)
- Did either generate downstream consumer complaints?

If there's a mismatch (e.g., the EDI 214 used a slightly different status code that downstream systems were keying on), the API translation needs adjustment before Stage 2.

This is a real engineering exercise per shipper-event, not a checkbox. Plan for it.

---

## 6. The deduplication problem — events that both OTR API and FBS might dispatch

Several events can be triggered from either OTR API (driven by TruckTools webhooks) or FBS (driven by internal business state changes). Without coordination, both might dispatch, creating duplicate transactions and confusing FK.

**Examples:**
- OTR API dispatches `LoadAssignedEvent` when TruckTools tells us a driver was assigned
- FBS *also* dispatches `LoadAssignedEvent` when its internal carrier-assignment workflow completes
- Both fire within seconds of each other, both succeed, FK gets the same assignment twice

**Three deduplication strategies, ordered by preference:**

### Strategy A — Source-of-truth designation per event type

Each event type has one designated source. Other sources don't dispatch it.

Example:
- `LoadCreatedEvent` → only OTR API dispatches (FBS lets OTR be source of truth)
- `LoadAssignedEvent` → only FBS dispatches (FBS knows about assignment earlier than TruckTools does)
- `LocationReportedEvent` → only OTR API dispatches (TruckTools is the only source of GPS)
- `LoadStatusEvent` → split by status type (TruckTools-driven statuses from OTR; business-driven statuses from FBS)

Implementation: documented policy, enforced by FBS not dispatching events that aren't its responsibility.

**Pros:** Simple. Cheap. No coordination at runtime.
**Cons:** Requires a clear policy document. Easy to drift if developers don't know the policy.

### Strategy B — Idempotency at the framework layer

Dispatcher checks "have we sent this same event for this load recently?" before dispatching. Recent = within N seconds.

Implementation: a fingerprint of `(VectorLoadId, EventType, key-event-fields)` checked against a small cache.

**Pros:** Forgiving — multiple callers can fire and the framework deduplicates.
**Cons:** False negatives possible (legitimate duplicate events that should fire — e.g., two genuine driver reassignments within 30 seconds). Adds latency.

### Strategy C — Adapter-layer idempotency

The vendor adapter checks before sending: "did I already send this exact payload to the vendor recently?"

**Pros:** Most precise; works even if framework has duplicates.
**Cons:** Adapter complexity grows; per-vendor logic.

**Recommendation: Strategy A.** Designate source-of-truth per event type at the start of Phase 2. Document it. The FBS code paths that don't dispatch certain events simply don't call `VendorDispatcher.Dispatch` for those events. Cheapest and clearest.

A small **dispatch policy** table in the framework documentation captures this:

| Event type | Source of truth | Why |
|---|---|---|
| `LoadCreatedEvent` | OTR API | OTR receives the load tender first |
| `LoadAssignedEvent` | FBS | FBS knows about driver assignment before TT acknowledges |
| `LocationReportedEvent` | OTR API | TruckTools is the only GPS source |
| `LoadStatusEvent` (TruckTools-driven codes) | OTR API | TT is the upstream |
| `LoadStatusEvent` (FBS business statuses) | FBS | FBS owns business statuses |
| `LoadTrackingStoppedEvent` | Either — last-write-wins | Cancellation can come from either side; we accept the slight duplication risk |
| `DocumentAvailableEvent` | VB.NET POD app, sometimes FBS | Depends on POD intake source — Glen confirms |
| `AppointmentScheduledEvent` (new) | FBS | FBS owns appointment booking |
| `AppointmentRescheduledEvent` (new) | FBS | Same |

This table becomes part of the Phase 2 implementation kickoff doc.

---

## 7. No duplicate sends — enforcement

Glen's hard rule: **"If we send via API, we don't also send via EDI for the same event."**

This is enforced in three places:

### 7.1 Switchboard configuration

In Stage 3 (cutover) and Stage 4, `EdiEnabled = false` is the configuration that prevents EDI from firing at all for cutover'd shippers. Most of the enforcement happens here — set the row, the EDI path stops running.

### 7.2 Dual-write awareness during Stages 1-2

Stages 1 and 2 deliberately dual-write. This is **the exception** to "no duplicate sends" because it's the validation mechanism. Operations must be aware:

- Stage 1: both fire, EDI is operational truth, API is observational
- Stage 2: both fire, API is operational truth, EDI is the safety net
- Stage 3+: only one fires

**Important — the "no duplicate sends" rule applies to the production state, not the validation state.** This is worth saying explicitly so a future engineer doesn't read the design doc, see dual-write, and conclude the rule was violated.

### 7.3 Audit visibility

Every dispatch is audited. The `FBS_ChannelRouting` lookup is also audited. So at any time, you can ask: "show me events for shipper X today where both API and EDI fired" — and the result tells you who's in dual-write (intended) vs. who has a misconfiguration (unintended).

A monitoring alert checks for "unintended dual-write" — i.e., a shipper-event combination where dual-write is happening but the `FBS_ChannelRouting` row says it shouldn't. That triggers ops investigation.

---

## 8. Per-shipper rollout — the practical timeline

For an organization with multiple shippers on FK + EDI, the cutover doesn't happen all at once. A practical pace:

| Week | Activity |
|---|---|
| 1 | Phase 2 design finalized; FBS implementation begins |
| 2-4 | FBS code: switchboard, dispatch call sites, audit. Internal testing with synthetic events. |
| 5 | Stage 1 (shadow) starts for **one pilot shipper** — preferably the lowest-risk one (small volume, technically capable counterparty, dispatcher team familiar with FK) |
| 5-7 | Pilot shipper in shadow mode; resolve semantic mismatches |
| 8-9 | Pilot shipper moves to Stage 2 (primary, dual-write) |
| 10-13 | Pilot shipper moves to Stage 3 (API only); 4-week stability window |
| 14 | Pilot shipper decommissioned (Stage 4) |
| 14 | Begin Stage 1 for the second shipper, applying lessons from the pilot |
| 14-26 | Cascade through remaining shippers, 2-4 weeks per shipper depending on event volume and complexity |
| 26+ | Decommission shared EDI infrastructure if no shippers depend on it |

This is **6 months of calendar time** for a typical mid-sized broker with 5-10 shippers. Most of it is stability windows, not engineering work. Tell stakeholders this up front — the engineering completes in weeks, the cutover takes months.

---

## 9. Risk register — Phase 2-specific

Risks I'd watch for during implementation. Each one is mitigated, not eliminated.

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Semantic mismatch between API and EDI causes FK to report different state on the same load | Medium | High | Stage 1 shadow mode catches this before cutover; documented per-shipper validation |
| FBS implementation introduces a bug that affects shippers NOT yet in API (the switchboard wrongly routes an EDI-only shipper to API) | Low | High | Default-deny switchboard config: routing rows must be explicit, no row = no API |
| API outage during Stage 3 (API-only) causes shipper visibility blackout | Low | High | Monitoring dashboard alerts before SLA breach; manual EDI fallback procedure (re-enable `EdiEnabled = true`) |
| Phase 2 development drains resources from Phase 1 production support | Medium | Medium | Defer Phase 2 start until 30+ days of clean Phase 1 production |
| Shipper objects to cutover for contractual reasons (their downstream system requires EDI) | Medium | Low | Some shippers stay on EDI permanently — Phase 2 doesn't require 100% cutover |
| Deduplication policy (Section 6) gets out of date as new event types are added | Medium | Medium | Document policy explicitly in code review checklist; add to onboarding |
| FBS's existing EDI code is brittle and breaks when modified for the switchboard | Medium | Medium | Switchboard sits *around* existing EDI code, not inside it — minimize changes to existing EDI generation paths |
| Audit volume in `VendorOutboundTransactions` grows significantly when FBS joins | Low | Low | Retention policy from #7 + index strategy handle this; reconfirm at Phase 2 kickoff |
| Multiple developers on FBS team interpret the switchboard differently | Low | Medium | Single design doc (this one) + code review enforcement |
| EDI trading partner is surprised by the cutover and breaks their integration | Low | Medium | Notify partners 30 days before each Stage 3 cutover; standard change-management |

---

## 10. What this design does NOT decide

Deliberately leaving these for Phase 2 kickoff, when Glen has more context:

| Decision | When to make it |
|---|---|
| FBS language (VB.NET? C#? Mixed?) and how the framework references are added | Phase 2 kickoff |
| Whether the switchboard is a SQL table, a config file, or hardcoded per-shipper class | Phase 2 kickoff |
| Whether the new event types (`AppointmentScheduledEvent`, etc.) ship with Phase 2 or wait until a shipper actually needs them | First shipper's event catalog review |
| The first pilot shipper | Sales + ops collaboration |
| Whether Phase 2 also handles inbound webhooks back to FBS (most likely no — OTR API receives webhooks per Phase 1, FBS reads from the audit log if it cares) | Phase 2 kickoff |
| Whether `VendorAdapterRegistry` config lives in FBS's own App.config or in a shared file | Phase 2 kickoff |

---

## 11. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-901 | FBS language and existing trigger architecture (event handlers, scheduled jobs, message bus?) | Phase 2 kickoff |
| O-902 | Catalog of FBS-originated EDI messages today, broken down by shipper | Stage 1 of any cutover |
| O-903 | Which shipper is the pilot? (Lowest-risk first) | Stage 1 |
| O-904 | Contract language with each shipper about API vs EDI (some contracts may mandate EDI) | Stage 3 of each cutover |
| O-905 | Should FBS dispatching add a new `SourceSystem` value (`"VectorFBS"`) to make the audit log distinguish FBS-dispatched events from OTR API-dispatched events? | Stage 1 (this is a small thing but worth confirming) |
| O-906 | Add `AppointmentScheduledEvent` and `AppointmentRescheduledEvent` to `Vendor.Common.Events` — defer until Phase 2 starts? | Phase 2 kickoff |
| O-907 | What does FBS use today for its outbound EDI generation? Will the switchboard's "EDI side" call existing FBS code as-is, or does that code need refactoring to be call-from-switchboard friendly? | Phase 2 kickoff |
| O-908 | Are there shippers where FK is contractually required to be on EDI (no cutover allowed)? | Phase 2 kickoff |

---

## 12. Done-when checklist (for Phase 2 itself, not this design doc)

Phase 2 is complete when:

- [ ] FBS references `Vendor.Common.dll` and `Vendor.FourKites.dll`; builds clean
- [ ] FBS switchboard table or config exists and is populated
- [ ] At least one event type dispatches from FBS through the framework
- [ ] First pilot shipper is in Stage 3 (API-only) for at least 4 weeks with no SLA breach
- [ ] The deduplication policy (Section 6 table) is documented in the engineering wiki
- [ ] Audit log shows `SourceSystem = 'VectorFBS'` rows alongside `OTR_API` rows
- [ ] Dashboard from #8 distinguishes FBS-originated from OTR-originated events (small filter addition)
- [ ] Cutover playbook (Section 5) has been executed for at least 2 shippers

**This deliverable (#9) is complete when:** the design above accurately represents what Phase 2 should do, and Glen agrees the framework supports it without changes from Phase 1. Implementation is a separate engagement.

---

## 13. How this design proves the framework

Phase 2 is the second proof point (after VB.NET POD in #6) that the framework abstraction is right:

- FBS is a different language stack from OTR API — same framework works
- FBS has business logic (the switchboard) that OTR API doesn't — framework doesn't push down into it
- New event types (`AppointmentScheduledEvent`, `AppointmentRescheduledEvent`) added without OTR API changes
- `VendorAPI_FK.VendorOutboundTransactions` becomes the single source of truth for both OTR-driven and FBS-driven dispatches
- Adding the second vendor (Phase 3) still requires zero changes to FBS — same as it required zero changes to OTR API and VB.NET

If any of these assumptions break when Phase 2 starts, the framework abstraction was wrong somewhere. The pressure tests in #10 should have caught it, but reality has the final word.

---

*End of Phase 2 FBS Design.*
