# OTR API Integration Insertion Points — Phase 1 (v2, framework-first)

**Document:** Deliverable #4 of 11 (rewrite — supersedes v1)
**Version:** 2.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** Deliverables #2 (refactor), #3 (OTR upgrade), and #10 (framework design) all complete
**Related decisions:** D-005, D-008, D-014, D-016, D-019, D-020, D-024, D-025

---

## 0. Purpose

This document shows **every place in OTR API where code must be added to dispatch events to the vendor framework**. After these changes:

- OTR API knows nothing about FourKites (or any specific vendor)
- OTR API speaks only in vendor-agnostic events
- The framework (`Vendor.Common.VendorDispatcher`) handles routing to whichever vendors are configured for the load's shipper
- A future vendor is added by writing one adapter class + adding one config row — **no OTR API changes**

**Glen executes the merges. Claude does not modify OTR API source.**

**Cardinal pattern:** *Tee-off, fail-safe, additive.* New dispatcher calls run AFTER existing operations succeed. If dispatch fails, the existing flow has already returned its normal response. Dispatch failures are logged but **never break existing Vector or TruckTools traffic**.

---

## 1. What this looks like to OTR API

To OTR API, the entire integration is **one type and one method**:

```csharp
VendorDispatcher.Instance.Dispatch(new SomeEvent { VectorLoadId = "...", ... });
```

That's it. OTR API never sees:
- `FourKitesClient`, `FourKitesAdapter`, or any vendor name
- `DispatcherBatch`, `LocationUpdate`, `IdentifierKey`, or any vendor payload type
- `BillToCode`, EDI 214 codes, API keys, or any vendor-specific concept
- The shipper-to-vendor routing logic
- Rate limits, retries, auth

The framework handles all of that. OTR API just announces what happened.

### 1.1 Two new project/DLL references

In `OTR API.csproj`, add references to:
- `Vendor.Common.dll`
- `Vendor.FourKites.dll`

OTR API only **uses** types from `Vendor.Common.Events` and `Vendor.Common.VendorDispatcher`. The `Vendor.FourKites` reference is needed at runtime because the adapter must be discoverable, but **OTR API code never imports `Vendor.FourKites.*` namespaces**. This is the contract that makes the resale story work — if you grep OTR API source for "FourKites" after the merge, you should find zero hits.

```xml
<Reference Include="Vendor.Common">
  <HintPath>..\..\FourKitesIntegration\fourkites-code\Vendor.Common\bin\Release\Vendor.Common.dll</HintPath>
  <Private>True</Private>
</Reference>
<Reference Include="Vendor.FourKites">
  <HintPath>..\..\FourKitesIntegration\fourkites-code\Vendor.FourKites\bin\Release\Vendor.FourKites.dll</HintPath>
  <Private>True</Private>
</Reference>
```

When vendor #2 arrives, you add a `<Reference Include="Vendor.Project44">` line. That's the only OTR API change.

---

## 2. Configuration changes — Web.config

### 2.1 appSettings

```xml
<appSettings>
  <!-- existing settings unchanged -->

  <!-- Vendor dispatch framework (new) -->
  <add key="VendorDispatch.Enabled" value="true" />
  <add key="VendorDispatch.AuditConnectionString"
       value="Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True" />
  <add key="VendorDispatch.FireAndForget" value="true" />
  <add key="VendorDispatch.SourceSystem" value="OTR_API" />
</appSettings>
```

**Key meanings:**

| Setting | Purpose |
|---|---|
| `VendorDispatch.Enabled` | Master kill switch. `false` disables ALL dispatch (every vendor). Emergency rollback. |
| `VendorDispatch.AuditConnectionString` | Connection to `VendorAPI_FK` database. The framework writes outbound transactions and reads ClientProfiles from here. |
| `VendorDispatch.FireAndForget` | If true, dispatcher returns immediately; vendor calls happen on background threads. **Recommended: true.** Adds no latency to OTR API responses. |
| `VendorDispatch.SourceSystem` | Stamped on every event so audit shows where the event originated. Each caller has its own value — OTR API is `OTR_API`, FBS will be `VectorFBS`, etc. |

**What's NOT here anymore (compared to v1):**

- No `FourKites.ApiKey`, `FourKites.BillToCode`, `FourKites.BaseUrl` — those live in the `ClientProfiles` table now (per shipper, encrypted ConfigJson)
- No `FourKites.Enabled` — vendor enable/disable is per-shipper via `ClientProfiles.IsActive`
- No `FourKites.TimeoutSeconds` — vendor timeouts live in their adapter's ConfigJson

This is what config-driven multi-vendor looks like. Adding a vendor changes the database, not the config file.

### 2.2 vendorAdapters config section

A custom config section registers which adapters are available. This is what makes adapter discovery config-driven (per Deliverable #10 Section 5.3).

```xml
<configuration>
  <configSections>
    <!-- existing sections unchanged -->
    <section name="vendorAdapters"
             type="Vendor.Common.Configuration.VendorAdaptersSection, Vendor.Common" />
  </configSections>

  <vendorAdapters>
    <adapters>
      <add vendorName="FourKites"
           type="Vendor.FourKites.Adapter.FourKitesAdapter, Vendor.FourKites" />
      <!-- When vendor #2 arrives, add a line here. That's the entire change. -->
    </adapters>
  </vendorAdapters>

  <!-- rest of Web.config unchanged -->
</configuration>
```

---

## 3. One new helper in OTR API — `TruckToolsStatusMapper.cs`

Before showing the controller merges, I need to address the **one piece of OTR API code that has to know about TruckTools' status codes**: the mapping from TT codes to the framework's `LoadStatusType` enum.

**Why this lives in OTR API, not the framework or the adapter:**

- TruckTools codes are **OTR API's input** — they come from TT webhooks hitting OTR API
- The framework doesn't know about TruckTools (it's vendor-agnostic; TT is one upstream of many possible)
- The FourKites adapter shouldn't know about TruckTools either — when FBS (a different upstream) starts dispatching events, those won't come from TT
- So: OTR API translates TT codes → framework enum; that translation is OTR-specific

This is a small file with a single static mapper. It produces `LoadStatusType` plus passes the raw TT code through in `SourceStatusCode` so adapters have fallback context.

**File:** `OTR API\DataClasses\TruckToolsStatusMapper.cs` (new)

```csharp
using System.Collections.Generic;
using Vendor.Common.Events;

namespace OTR_API.DataClasses
{
    /// <summary>
    /// Maps TruckTools status codes to the framework's vendor-agnostic LoadStatusType.
    /// The original TT code is always passed through in LoadStatusEvent.SourceStatusCode
    /// for adapters that need finer-grained information.
    ///
    /// Returns LoadStatusType.Other when the code doesn't fit a clean bucket.
    /// Adapters should consult SourceStatusCode in that case.
    /// </summary>
    public static class TruckToolsStatusMapper
    {
        private static readonly Dictionary<string, LoadStatusType> _map =
            new Dictionary<string, LoadStatusType>(System.StringComparer.OrdinalIgnoreCase)
        {
            // TODO: Populate from audit log analysis (Open Item O-002 in strategy doc).
            // The values below are illustrative — verify against real TT codes.
            { "DISPATCHED",        LoadStatusType.Dispatched },
            { "EN_ROUTE_PICKUP",   LoadStatusType.InTransit },
            { "ARRIVED_PICKUP",    LoadStatusType.ArrivedAtPickup },
            { "DEPARTED_PICKUP",   LoadStatusType.DepartedPickup },
            { "EN_ROUTE_DELIVERY", LoadStatusType.InTransit },
            { "ARRIVED_DELIVERY",  LoadStatusType.ArrivedAtDelivery },
            { "DEPARTED_DELIVERY", LoadStatusType.DepartedDelivery },
            { "DELIVERED",         LoadStatusType.Delivered },
            { "EXCEPTION",         LoadStatusType.Exception },
        };

        public static LoadStatusType Map(string truckToolsCode)
        {
            if (string.IsNullOrWhiteSpace(truckToolsCode)) return LoadStatusType.Other;
            return _map.TryGetValue(truckToolsCode, out var type) ? type : LoadStatusType.Other;
        }
    }
}
```

**How to populate the dictionary:** Section 7 below shows the SQL query against the audit log.

---

## 4. Insertion points — side-by-side merges

For each insertion point: the existing code excerpt, the proposed new code, and what to test after applying.

### 4.1 Insertion Point #1 — `TruckerToolsController.PostLoad`

**File:** `Controllers\TruckerToolsController.cs`
**Existing line range:** ~270–305
**What this endpoint does:** Vector FBS POSTs a matched-trip Load; OTR API saves it locally and forwards to TruckTools.
**Framework event to dispatch:** `LoadCreatedEvent`

#### Existing code

```csharp
[HttpPost]
public LoadResponse PostLoad([FromBody]Load load)
{
    LoadResponse response = new LoadResponse();

    DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();
    DataLoadMatch dl = new DataLoadMatch();

    try
    {
        int LoadID = dl.InsertLoad(load);

        try
        {
            WebCallFunctions wc = new WebCallFunctions();
            Task<LoadResponse> task1 = Task.Run(() => { return wc.PostTTLoad(load); });
            response = task1.Result;
            response.LoadID = LoadID;
            dtt.InsertLoadResponse(response);
        }
        catch (Exception ex)
        {
            response.Message = "Error Posting Load - " + ex.Message;
        }
    }
    catch(Exception ex)
    {
        response.Message = "Error Saving Load - " + ex.Message;
    }

    return response;
}
```

#### Proposed code

```csharp
[HttpPost]
public LoadResponse PostLoad([FromBody]Load load)
{
    LoadResponse response = new LoadResponse();

    DataTruckerToolsMatch dtt = new DataTruckerToolsMatch();
    DataLoadMatch dl = new DataLoadMatch();

    try
    {
        int LoadID = dl.InsertLoad(load);

        try
        {
            WebCallFunctions wc = new WebCallFunctions();
            Task<LoadResponse> task1 = Task.Run(() => { return wc.PostTTLoad(load); });
            response = task1.Result;
            response.LoadID = LoadID;
            dtt.InsertLoadResponse(response);

            // ─── NEW: dispatch to vendor framework ──────────────
            Vendor.Common.VendorDispatcher.Instance.Dispatch(
                new Vendor.Common.Events.LoadCreatedEvent
                {
                    VectorLoadId   = load.VectorID.ToString(),
                    SourceSystem   = "OTR_API",
                    Mode           = load.mode,           // verify property names (O-202)
                    EquipmentType  = load.equipmentType,
                    Weight         = load.weight,
                    WeightUnit     = load.weightUnit,
                    Origin         = MapStop(load.originStop),       // see helper below
                    Destination    = MapStop(load.destinationStop),
                    References     = MapReferences(load.references)
                });
            // ────────────────────────────────────────────────────
        }
        catch (Exception ex)
        {
            response.Message = "Error Posting Load - " + ex.Message;
        }
    }
    catch(Exception ex)
    {
        response.Message = "Error Saving Load - " + ex.Message;
    }

    return response;
}
```

**Helper methods** (add to the same controller file or a dedicated helper):

```csharp
private static Vendor.Common.Events.StopInfo MapStop(SomeOtrStopType stop)
{
    if (stop == null) return null;
    return new Vendor.Common.Events.StopInfo
    {
        Role          = MapStopRole(stop.role),
        Name          = stop.name,
        AddressLine1  = stop.address1,
        City          = stop.city,
        State         = stop.state,
        PostalCode    = stop.zip,
        Country       = stop.country,
        ScheduledArrivalUtc   = stop.scheduledArrival?.ToUniversalTime(),
        ScheduledDepartureUtc = stop.scheduledDeparture?.ToUniversalTime()
    };
}

private static Vendor.Common.Events.StopRole MapStopRole(string otrRole)
{
    switch (otrRole?.ToUpperInvariant())
    {
        case "PICKUP":   return Vendor.Common.Events.StopRole.Pickup;
        case "DELIVERY": return Vendor.Common.Events.StopRole.Delivery;
        default:         return Vendor.Common.Events.StopRole.Intermediate;
    }
}

private static List<Vendor.Common.Events.ReferenceNumber> MapReferences(IEnumerable<SomeOtrRefType> refs)
{
    if (refs == null) return null;
    var list = new List<Vendor.Common.Events.ReferenceNumber>();
    foreach (var r in refs)
        list.Add(new Vendor.Common.Events.ReferenceNumber { Type = r.type, Value = r.value });
    return list;
}
```

**Property name caveats (O-202):** I don't have the full `Load` model in hand. Property names like `mode`, `equipmentType`, `weight`, `originStop`, `references` are guesses — Glen verifies against the actual `Models\Loads.cs` and adjusts.

**Why this placement:** Same reasoning as v1. The dispatch goes inside the inner try, after `dtt.InsertLoadResponse(response)`. Only loads we successfully posted to TruckTools get announced to the framework.

**What to test:**
1. POST a test load to `/api/truckertools/postload` → returns 200 with `LoadID` populated
2. Verify existing local DB row and TruckTools forward (existing behavior preserved)
3. Verify new row in `VendorAPI_FK.VendorOutboundTransactions` with `EventTypeName = 'LoadCreatedEvent'`, `VendorName = 'FourKites'` (because FK is the only configured vendor today), `Status` progressing through `PENDING` → `ACK`
4. Toggle `VendorDispatch.Enabled = false` → confirm OTR API behaves exactly as before, no FK transaction row

---

### 4.2 Insertion Point #2 — `TruckerToolsTrackingController.TrackLoad`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~17–55
**What this endpoint does:** Vector tells OTR API to start tracking — driver/truck/trailer info is attached.
**Framework event:** `LoadAssignedEvent`

#### Proposed addition

Inside the inner try block, after `dtt.InsertLoadResponse(response);`:

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.PostTrackLoad(load); });
    response = task1.Result;
    response.response.TrackingID = LoadID;
    dtt.InsertLoadResponse(response);

    // ─── NEW: dispatch to vendor framework ──────────────
    Vendor.Common.VendorDispatcher.Instance.Dispatch(
        new Vendor.Common.Events.LoadAssignedEvent
        {
            VectorLoadId = load.VectorID.ToString(),
            SourceSystem = "OTR_API",
            Carrier = new Vendor.Common.Events.CarrierInfo
            {
                Scac = load.carrierSCAC,
                Name = load.carrierName
            },
            Driver = new Vendor.Common.Events.DriverInfo
            {
                Name  = load.driverName,
                Phone = load.driverCell
            },
            Equipment = new Vendor.Common.Events.EquipmentInfo
            {
                TruckNumber   = load.truckNumber,
                TrailerNumber = load.trailerNumber
            }
        });
    // ────────────────────────────────────────────────────
}
catch (Exception ex)
{
    response.response.Message = "Error Posting Load - " + ex.Message;
}
```

**What to test:**
1. POST to `/api/truckertools/trackload` with a Load that has carrier/driver/truck/trailer populated
2. Verify TruckTools starts tracking (existing behavior)
3. Verify `VendorOutboundTransactions` row with `EventTypeName = 'LoadAssignedEvent'`
4. Verify FourKites web UI shows the driver and equipment on the load

---

### 4.3 Insertion Point #3 — `TruckerToolsTrackingController.UpdateTrackLoad`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~62–105
**What this endpoint does:** Updates an already-tracking load (driver swap, truck change).
**Framework event:** `LoadAssignedEvent` (same shape as TrackLoad — assignments are idempotent across vendors)

#### Proposed addition

Identical to Insertion Point #2 — paste the exact same `VendorDispatcher.Instance.Dispatch(new LoadAssignedEvent { ... })` block in the same relative position (after `dtt.InsertLoadResponse(response);`).

**Why same event:** A reassignment is just an assignment that overwrites the previous one. Every reasonable vendor handles this idempotently. If a future vendor cares about "this was a *change*", we'd add a `LoadReassignedEvent` later — but it's additive, doesn't break anything.

**What to test:** Same as Insertion Point #2 but for updates.

---

### 4.4 Insertion Point #4 — `TruckerToolsTrackingController.SendStatus`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~152–310 (long method)
**What this endpoint does:** TruckTools webhooks status + location every 15 minutes per active load. Highest-volume endpoint.
**Framework events:** Up to TWO events per call:
- `LocationReportedEvent` if location data is present
- `LoadStatusEvent` if a status code is present

**This is where fire-and-forget matters most.** TT expects fast webhook ACKs.

#### Proposed addition

Near the end of the method, after the existing audit and response logic, before the final `return response;`:

Look for this section (around line 290–310):

```csharp
                    try
                    {
                        int responseID = dtt.InsertLoadTrackingStatusResponse(response);
                        // ... commented-out email code ...
                    }
                    catch(Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse");
                    }


                    if(!response.status)
                    {

                    }
```

Add the dispatch block between the `catch` and the empty `if (!response.status)` block:

```csharp
                    try
                    {
                        int responseID = dtt.InsertLoadTrackingStatusResponse(response);
                        // ... commented-out email code ...
                    }
                    catch(Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse");
                    }

                    // ─── NEW: dispatch to vendor framework ──────────────
                    if (response.status)
                    {
                        DispatchTruckToolsStatusUpdate(lc);
                    }
                    // ────────────────────────────────────────────────────

                    if(!response.status)
                    {

                    }
```

The dispatch logic is moved into a private helper method on the controller (or a static helper class) to keep the long `SendStatus` method readable:

```csharp
private static void DispatchTruckToolsStatusUpdate(StatusUpdate lc)
{
    var vectorLoadId = lc.loadNumber;
    var dispatcher   = Vendor.Common.VendorDispatcher.Instance;

    // 1. Location event — fire if any location data is present
    var loc = lc.latestLocation
           ?? lc.latestStatus?.location
           ?? lc.status?.location;
    if (loc != null && !string.IsNullOrEmpty(loc.lat) && !string.IsNullOrEmpty(loc.lon))
    {
        dispatcher.Dispatch(new Vendor.Common.Events.LocationReportedEvent
        {
            VectorLoadId  = vectorLoadId,
            SourceSystem  = "OTR_API",
            Latitude      = loc.lat,
            Longitude     = loc.lon,
            LocatedAtUtc  = ParseTruckToolsTimestamp(loc.timeStamp),
            City          = loc.city,
            State         = loc.state
        });
    }

    // 2. Status event — fire if a status code is present
    var statusInfo = lc.latestStatus ?? lc.status;
    if (statusInfo != null && !string.IsNullOrEmpty(statusInfo.code))
    {
        var statusType = OTR_API.DataClasses.TruckToolsStatusMapper.Map(statusInfo.code);

        dispatcher.Dispatch(new Vendor.Common.Events.LoadStatusEvent
        {
            VectorLoadId           = vectorLoadId,
            SourceSystem           = "OTR_API",
            StatusType             = statusType,
            SourceStatusCode       = statusInfo.code,         // raw TT code preserved for adapters
            SourceStatusDescription = statusInfo.name,
            StatusTimeUtc          = ParseTruckToolsTimestamp(statusInfo.timeStamp),
            AtStop = statusInfo.location != null
                ? new Vendor.Common.Events.StopInfo
                    {
                        City  = statusInfo.location.city,
                        State = statusInfo.location.state
                    }
                : null
        });
    }
}

private static DateTime ParseTruckToolsTimestamp(string raw)
{
    // TT format from existing code: "MM/dd/yyyy HH:mm:ss tt K"
    if (string.IsNullOrEmpty(raw)) return DateTime.UtcNow;
    return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.AssumeUniversal |
                             System.Globalization.DateTimeStyles.AdjustToUniversal,
                             out var dt)
           ? dt
           : DateTime.UtcNow;
}
```

**Notice what's NOT in this code:**
- No EDI 214 codes anywhere — the framework's `LoadStatusType` enum is vendor-neutral
- No `BillToCode` — that's in the FK adapter's ClientProfile.ConfigJson
- No FourKites reference of any kind

The FourKites adapter, when it receives the `LoadStatusEvent`, looks at `StatusType` and `SourceStatusCode` and translates them to EDI 214. That translation is internal to `Vendor.FourKites.Mapping.Edi214Mapper`. OTR API has no idea EDI 214 exists.

**Recommended `VendorDispatch.FireAndForget = true`** (the default). At ~15-minute cadence across many loads, vendor API latency must not delay TT webhook ACKs.

**What to test:**
1. Simulate or wait for a TT webhook → `/api/truckertools/sendstatus`
2. Verify response time stays similar to baseline (within 100ms — fire-and-forget should add ~negligible time)
3. Verify all existing TT-side persistence (existing behavior)
4. Verify `VendorOutboundTransactions` shows 1 or 2 new rows (location event + optional status event) within ~5 seconds
5. Verify the FK adapter correctly translated `StatusType.ArrivedAtPickup` → EDI 214 code `X3` (check the row's `RequestPayload`)
6. Push 50+ updates rapidly; verify FK adapter throttles at 60/min without backpressure spilling into OTR API response times

---

### 4.5 Insertion Point #5 — `TruckerToolsTrackingController.CancelLoadTracking`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~110–148
**What this endpoint does:** Vector cancels tracking on a load.
**Framework event:** `LoadTrackingStoppedEvent`

#### Proposed addition

Inside the inner try, after `dtt.InsertLoadResponse(response);`:

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.CancelLoadTracking(load); });
    response = task1.Result;
    response.response.TrackingID = TrackingID;
    dtt.InsertLoadResponse(response);

    // ─── NEW: dispatch to vendor framework ──────────────
    Vendor.Common.VendorDispatcher.Instance.Dispatch(
        new Vendor.Common.Events.LoadTrackingStoppedEvent
        {
            VectorLoadId = load.VectorID.ToString(),
            SourceSystem = "OTR_API",
            Reason       = "DISPATCHER_STOPPED"
        });
    // ────────────────────────────────────────────────────
}
catch (Exception ex)
{
    response.response.Message = "Error Cancelling Load - " + ex.Message;
}
```

**Note on `Reason`:** The framework enum doesn't formalize reasons; it's a free-text string for adapters to use as they see fit. `"DISPATCHER_STOPPED"` indicates this came from an operator cancellation (vs. e.g. `"DELIVERED"` from final-stop logic). Future events may add this — for now, OTR's cancellation is always operator-initiated, so the constant string is fine.

**What to test:**
1. POST to `/api/truckertools/cancelloadtracking` for an active load
2. Verify TruckTools stops tracking (existing behavior)
3. Verify `VendorOutboundTransactions` row with `EventTypeName = 'LoadTrackingStoppedEvent'`
4. Verify FK shows the load as no-longer-tracking

---

## 5. Summary of changes to OTR API

The complete list of files Glen touches:

| File | Change type | Lines added |
|---|---|---|
| `OTR API.csproj` | Add 2 DLL references | 6 lines |
| `Web.config` | Add 4 `<add key=>` entries in appSettings; add `<vendorAdapters>` section + section declaration | ~12 lines |
| `DataClasses\TruckToolsStatusMapper.cs` | **NEW FILE** — small (~30 lines) | ~30 lines |
| `Controllers\TruckerToolsController.cs` | Add `LoadCreatedEvent` dispatch in PostLoad + 3 helper methods | ~40 lines |
| `Controllers\TruckerToolsTrackingController.cs` | Add `LoadAssignedEvent` dispatch in 2 methods, `LocationReportedEvent` + `LoadStatusEvent` in SendStatus (via helper), `LoadTrackingStoppedEvent` in CancelLoadTracking + 2 helper methods | ~90 lines |

**Total: ~180 lines added across 5 files. Zero lines removed. Zero existing logic modified.**

Compared to v1: slightly less code in OTR API (no FourKitesTeeOff class), but more "shape" — explicit event construction in each place. Tradeoff is worth it: every line in OTR API is now vendor-agnostic.

**The grep test:** after merge, `grep -ri "fourkites" .` in the OTR API source folder should return **zero matches** (apart from references in the .csproj's DLL HintPath). That's the proof the framework boundary holds.

---

## 6. Fire-and-forget recommendation

The framework's single `VendorDispatch.FireAndForget` setting (in Web.config) controls behavior for all dispatches from this caller. For OTR API:

**Recommended: `true`** (the default).

Reasoning:
- SendStatus is the high-volume endpoint and the one most sensitive to latency
- Other endpoints (PostLoad, TrackLoad, etc.) are low-volume and don't suffer from non-blocking dispatch
- Vendor failures shouldn't impact OTR API responses
- The audit log is the source of truth for "did dispatch succeed" — no need for synchronous awareness in the controller

If you ever want per-endpoint control, the dispatcher exposes `DispatchAsync()` as an alternative — controllers that *want* to await it can call that instead. None do in Phase 1.

---

## 7. Populating the TruckTools status code map

Section 3 has placeholder mappings. The real codes come from your audit log.

Run this against the OTR API audit database (likely `VectorOTR_TT`):

```sql
SELECT DISTINCT
    s.code        AS TruckToolsCode,
    s.name        AS Description,
    COUNT(*)      AS Occurrences,
    MIN(s.timeStamp) AS EarliestSeen,
    MAX(s.timeStamp) AS LatestSeen
FROM dbo.LoadTrackingStatusInfo s    -- exact table name needs verification
WHERE s.code IS NOT NULL
  AND s.timeStamp > DATEADD(DAY, -90, GETDATE())
GROUP BY s.code, s.name
ORDER BY Occurrences DESC;
```

For each code that appears with meaningful frequency, decide its `LoadStatusType`:

| Real TT code (from audit) | Maps to |
|---|---|
| (high frequency code) | One of: Dispatched / ArrivedAtPickup / DepartedPickup / InTransit / ArrivedAtDelivery / DepartedDelivery / Delivered / Exception / Other |

Rare codes (< 0.1% of total) can map to `Other` — adapters fall back to `SourceStatusCode` for those.

Update `TruckToolsStatusMapper._map` with the real mappings.

---

## 8. ClientProfile setup — one-time DB insert

Before any dispatch will actually reach FourKites, a `ClientProfile` row must exist in `VendorAPI_FK.dbo.ClientProfiles`.

Phase 1 needs one row:

```sql
INSERT INTO dbo.ClientProfiles (ShipperCode, VendorName, IsActive, EnabledEvents, ConfigJson)
VALUES (
    'VECTOR_DEFAULT',
    'FourKites',
    1,
    'LoadCreatedEvent,LoadAssignedEvent,LocationReportedEvent,LoadStatusEvent,LoadTrackingStoppedEvent,DocumentAvailableEvent',
    '{
        "apiKey": "<ENCRYPTED OR PLAINTEXT API KEY>",
        "billToCode": "VECTOR-001",
        "baseUrl": "https://api.fourkites.com",
        "timeoutSeconds": 15
    }'
);
```

(Real DDL for the `ClientProfiles` table comes in Deliverable #7. This is the data, not the schema.)

For sandbox testing, point `baseUrl` at the FK sandbox URL and use sandbox credentials.

**API key encryption:** Open Item O-502. Phase 1 may ship with plaintext if encryption infrastructure isn't ready; SQL Server `ALWAYS ENCRYPTED` column on `ConfigJson` is a reasonable target for production.

---

## 9. Merge order — recommended sequence

Same as v1, restated for completeness. Stage merges so each is validated before the next:

1. **Wire up infrastructure first.** Add DLL references, `TruckToolsStatusMapper.cs`, Web.config changes — **but set `VendorDispatch.Enabled = false`**. Build and deploy. Verify nothing broke.
2. **Insert the ClientProfile row** for FourKites in `VendorAPI_FK`.
3. **Enable dispatch and merge Insertion Point #2 (TrackLoad).** Lowest volume; easy to verify, easy to roll back.
4. **Merge Insertion Point #3 (UpdateTrackLoad).** Trivial after #2 works.
5. **Merge Insertion Point #5 (CancelLoadTracking).** Same volume profile.
6. **Merge Insertion Point #1 (PostLoad).** Larger event payload (stops, references); verify load fields map correctly.
7. **Merge Insertion Point #4 (SendStatus).** HIGHEST volume — leave for last after the others are proven. Fire-and-forget validation matters here.

After each step: deploy, run smoke tests against the affected endpoint, watch the audit log for 24 hours before proceeding.

---

## 10. Pre-merge checklist

Before applying ANY merge:

- [ ] Deliverables #2 (refactor), #3 (OTR upgrade), and #10 (framework design) are complete
- [ ] `Vendor.Common.dll` and `Vendor.FourKites.dll` exist as built artifacts
- [ ] `VendorAPI_FK` database exists with `ClientProfiles` and `VendorOutboundTransactions` tables (Deliverable #7)
- [ ] FourKites API key (sandbox) obtained and saved to `ClientProfiles.ConfigJson`
- [ ] BillToCode confirmed with FourKites CSM
- [ ] TT status code → `LoadStatusType` mapping populated in `TruckToolsStatusMapper`
- [ ] Vendor.FourKites SmokeTest passes against sandbox

---

## 11. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-202 | Property name verification on OTR's Load model (truckNumber, trailerNumber, driverName, driverCell, carrierSCAC, mode, equipmentType, weight, originStop, destinationStop, references) | Building any controller dispatch |
| O-203 | Timestamp format from TT — confirm `ParseTruckToolsTimestamp` handles all variants seen in production | Insertion Point #4 going live |
| O-401 | Are `originStop`, `destinationStop`, and `references` represented on the Load model the way `LoadCreatedEvent` expects, or do they need richer mapping? | Insertion Point #1 going live; can ship stub mapping first |
| O-402 | What's the shipper code source for a load? OTR's Load model has no `shipperCode` evident. For Phase 1, leaving `ShipperCode` off the event is fine — `LoadShipperResolver` falls back to `VECTOR_DEFAULT`. But the resolver query needs to know where to look eventually. | Multi-shipper support; Phase 2+ |

---

## 12. Done-when checklist

Mark this deliverable complete when:

- [ ] OTR API references `Vendor.Common.dll` and `Vendor.FourKites.dll` and compiles
- [ ] `TruckToolsStatusMapper.cs` added and populated from audit log analysis
- [ ] `Web.config` updated with `VendorDispatch.*` settings and `vendorAdapters` section
- [ ] One `ClientProfile` row exists in `VendorAPI_FK` for `(VECTOR_DEFAULT, FourKites)` with the configured EnabledEvents
- [ ] All five insertion points merged
- [ ] **`grep -ri "fourkites" .` in OTR API source returns zero matches** (apart from `.csproj` HintPath)
- [ ] With `VendorDispatch.Enabled = false`: OTR API behaves identically to pre-merge (regression test)
- [ ] With `VendorDispatch.Enabled = true`: each insertion point produces a `VendorOutboundTransactions` row when the corresponding endpoint is hit
- [ ] FourKites web UI reflects: new shipments from PostLoad, driver/equipment from TrackLoad, locations and statuses from SendStatus, stopped tracking from CancelLoadTracking
- [ ] 24-hour soak test in staging shows expected transaction volume and no errors above baseline
- [ ] Production deployed
- [ ] Audit log shows successful (Status = ACK) transactions for at least one of each event type

---

## 13. What this deliverable proves

This is the contract that makes the resale story real:

- OTR API source contains **zero** mentions of "FourKites"
- All FourKites-specific logic (EDI 214 mapping, BillToCode, IdentifierKey shape, API auth) is in `Vendor.FourKites.dll`
- When vendor #2 ships, OTR API does not change
- Adding vendor #2 is: write `Vendor.Project44.Adapter.Project44Adapter`, add `<add vendorName="Project44" type="..." />` to Web.config, insert one `ClientProfile` row
- A customer evaluating the platform can read OTR API's source and immediately understand: "events flow through the dispatcher; vendors plug in via configuration"

That's the framework, demonstrated end-to-end with one real vendor.

---

*End of OTR API Insertion Points (v2, framework-first).*
