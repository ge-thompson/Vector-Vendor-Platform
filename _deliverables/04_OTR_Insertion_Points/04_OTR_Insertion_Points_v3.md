# OTR API Insertion Points — Phase 1 (v3, ready-to-execute)

**Document:** Deliverable #4 of 11 (v3 — supersedes v2)
**Version:** 3.0
**Date:** June 16, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** ✅ All complete
- ✅ Vendor.Common framework built and tested (74 tests green)
- ✅ Vendor.FourKites adapter built and tested (50 tests green)
- ✅ VendorAPI_FK database deployed to LocalDB
- ✅ Seed ClientProfile row exists for (VECTOR_DEFAULT, FourKites)

**Related decisions:** D-005, D-008, D-014, D-018, D-024

---

## 0. What changes in this version vs. v2

v2 was written when the framework was a design. v3 is written now that it's real code we've held in our hands. Differences:

1. **Property names verified against actual code.** Earlier guesses like `load.originStop` and `load.shipper.code` are replaced with what's actually there (`load.pickup`, `load.shipper.ShipperId`).
2. **Five concrete insertion points, in order.** Not a survey — a checklist.
3. **TruckToolsStatusMapper.cs is included as final source code,** not pseudocode.
4. **Real Web.config XML you can paste.**
5. **One simplification:** the old `Reference Include` HintPath approach was for compiled DLLs; since OTR API will live next to `Vendor.Common.csproj` and `Vendor.FourKites.csproj` in your solution, ProjectReference is cleaner.

---

## 1. Quick orientation

### What OTR API will look like after these merges

Five new dispatch calls. Each is the same shape:

```csharp
Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
    new Vendor.Common.Events.SomeEvent
    {
        VectorLoadId = load.VectorID.ToString(),
        SourceSystem = "OTR_API",
        // event-specific fields
    });
```

That's it. OTR API never mentions "FourKites" by name. If you `grep -ri "fourkites" "OTR API"` after these merges, you should get **zero hits** outside the project file's DLL reference.

### Where the calls go

| # | File (under `_OTR_API\`) | Method | Event | Volume |
|---|---|---|---|---|
| 1 | `Controllers\TruckerToolsController.cs` | `PostLoad` | `LoadCreatedEvent` | Low |
| 2 | `Controllers\TruckerToolsTrackingController.cs` | `TrackLoad` | `LoadAssignedEvent` | Low |
| 3 | `Controllers\TruckerToolsTrackingController.cs` | `UpdateTrackLoad` | `LoadAssignedEvent` | Low |
| 4 | `Controllers\TruckerToolsTrackingController.cs` | `CancelLoadTracking` | `LoadTrackingStoppedEvent` | Low |
| 5 | `Controllers\TruckerToolsTrackingController.cs` | `SendStatus` | `LoadStatusEvent` + `LocationReportedEvent` | **HIGH** (every 15 min × every active load) |

Plus one new tiny file: `_OTR_API\DataClasses\TruckToolsStatusMapper.cs`.

Plus three Web.config / .csproj changes in `_OTR_API\`.

---

## 2. Step 1 — Project references and Web.config

### 2.1 Project references (OTR API.csproj)

The cleanest setup: add `Vendor.Common.csproj` and `Vendor.FourKites.csproj` to the OTR API solution as project references. From Visual Studio with the OTR API solution open:

- Solution Explorer → right-click solution → **Add → Existing Project**
- Navigate to `_build\Vendor.Common\Vendor.Common.csproj` — Add
- Repeat for `_build\Vendor.FourKites\Vendor.FourKites.csproj`
- Right-click `OTR API` project → **Add → Project Reference** → check `Vendor.Common` and `Vendor.FourKites` → OK

After this you should see `Vendor.Common` and `Vendor.FourKites` listed under `OTR API > References`.

**Why both must be referenced:** OTR API only **uses** types from `Vendor.Common.Events` and `Vendor.Common.Dispatch`. But `Vendor.FourKites.dll` must be present at runtime because the registry needs to instantiate `FourKitesAdapter` via reflection from config. The reference ensures the DLL is copied to OTR API's `bin\` folder on build.

### 2.2 Web.config — appSettings additions

Add these four keys to the existing `<appSettings>` section:

```xml
<appSettings>
  <!-- existing keys unchanged -->

  <!-- Vendor dispatch framework (new) -->
  <add key="VendorDispatch.Enabled"
       value="false" />  <!-- START with false; flip to true after smoke testing -->
  <add key="VendorDispatch.AuditConnectionString"
       value="Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True" />
  <add key="VendorDispatch.FireAndForget"
       value="true" />
  <add key="VendorDispatch.SourceSystem"
       value="OTR_API" />
</appSettings>
```

**Note on connection string:** the server name above is a guess for Vector's production. Use whatever connection string matches Vector's deployment — for dev/test it would be the LocalDB string we used during smoke testing.

**Note on `VendorDispatch.Enabled = false` initially:** keep this false until all merges are deployed and you've verified no regressions. Flipping it to true is the moment dispatch actually starts firing.

### 2.3 Web.config — vendorAdapters config section

Two changes. First, declare the section type at the top:

```xml
<configuration>
  <configSections>
    <!-- existing sections unchanged -->
    <section name="vendorAdapters"
             type="Vendor.Common.Configuration.VendorAdaptersSection, Vendor.Common" />
  </configSections>
```

Then add the section itself anywhere inside `<configuration>` (just before `</configuration>` is fine):

```xml
  <vendorAdapters>
    <adapters>
      <add vendorName="FourKites"
           adapterType="Vendor.FourKites.FourKitesAdapter, Vendor.FourKites"
           inboundProcessorType="Vendor.FourKites.Webhooks.FourKitesWebhookProcessor, Vendor.FourKites"
           webhookValidatorType="Vendor.FourKites.Webhooks.FourKitesWebhookSignatureValidator, Vendor.FourKites" />
      <!-- When vendor #2 arrives, add a single line here. Nothing else changes. -->
    </adapters>
  </vendorAdapters>
</configuration>
```

### 2.4 Global.asax.cs — Application_Start

The dispatcher singleton must be configured once at startup. Open `OTR API\Global.asax.cs` and add to `Application_Start`:

```csharp
protected void Application_Start()
{
    // ... existing initialization ...

    // ─── NEW: initialize vendor dispatch framework ──────────
    try
    {
        Vendor.Common.Dispatch.VendorDispatcher.Configure(
            errorHandler: ex =>
            {
                // Route dispatch errors to existing OTR API error audit log
                try
                {
                    new OTR_API.DataClasses.DataAudit()
                        .InsertErrorAuditLog(ex.ToString(), "VendorDispatch");
                }
                catch { /* never let logging break the app */ }
            });
    }
    catch (Exception ex)
    {
        // Fail loudly at startup if config is bad — never silently disable
        System.Diagnostics.EventLog.WriteEntry(
            "Application",
            "VendorDispatch initialization failed: " + ex.ToString(),
            System.Diagnostics.EventLogEntryType.Error);
        throw;
    }
    // ────────────────────────────────────────────────────────
}
```

That's all the startup wiring needed for outbound. The inbound (webhook controller + correlator) is a separate piece — see Deliverable #5.

---

## 3. Step 2 — Create the TruckTools status code mapper

OTR API receives status updates from TruckTools with TT-specific codes. Before dispatching, we translate them to the framework's vendor-agnostic `LoadStatusType`. The translation lives in OTR API because it's about OTR API's *upstream* (TruckTools), not its downstream vendors.

**New file:** `OTR API\DataClasses\TruckToolsStatusMapper.cs`

```csharp
using System;
using System.Collections.Generic;
using Vendor.Common.Events;

namespace OTR_API.DataClasses
{
    /// <summary>
    /// Translates TruckTools' status codes (and status names) into the
    /// framework's vendor-agnostic LoadStatusType enum.
    ///
    /// Both the type and the original raw code are sent in the dispatched event,
    /// so adapters that need finer granularity (FK using EDI 214, etc.) can pass
    /// the source code through.
    ///
    /// PHASE 1 NOTE: the mappings below cover the most common TT codes based on
    /// observed traffic. Refine after collecting audit data on what TT actually
    /// sends (Open Item O-002). To populate from real data, run:
    ///
    ///   SELECT DISTINCT s.code, s.name, COUNT(*) AS Hits
    ///   FROM dbo.LoadTrackingStatusInfo s
    ///   WHERE s.timeStamp > DATEADD(DAY, -90, GETDATE())
    ///   GROUP BY s.code, s.name ORDER BY Hits DESC;
    /// </summary>
    public static class TruckToolsStatusMapper
    {
        private static readonly Dictionary<string, LoadStatusType> _byCode =
            new Dictionary<string, LoadStatusType>(StringComparer.OrdinalIgnoreCase)
        {
            // TruckTools codes observed in audit. Refine as more codes are seen.
            { "DISPATCHED",        LoadStatusType.Dispatched },
            { "EN_ROUTE_PICKUP",   LoadStatusType.InTransit },
            { "ARRIVED_PICKUP",    LoadStatusType.ArrivedAtPickup },
            { "DEPARTED_PICKUP",   LoadStatusType.DepartedPickup },
            { "EN_ROUTE_DELIVERY", LoadStatusType.InTransit },
            { "ARRIVED_DELIVERY",  LoadStatusType.ArrivedAtDelivery },
            { "DEPARTED_DELIVERY", LoadStatusType.DepartedDelivery },
            { "DELIVERED",         LoadStatusType.Delivered },
            { "EXCEPTION",         LoadStatusType.Exception },
            { "CANCELLED",         LoadStatusType.Exception },
        };

        /// <summary>
        /// Maps a TT code to LoadStatusType. Returns Other for unknown codes
        /// (the dispatched event will carry the raw code in SourceStatusCode
        /// so adapters can fall back to it).
        /// </summary>
        public static LoadStatusType Map(string truckToolsCode)
        {
            if (string.IsNullOrWhiteSpace(truckToolsCode))
                return LoadStatusType.Other;

            return _byCode.TryGetValue(truckToolsCode, out var type)
                ? type
                : LoadStatusType.Other;
        }
    }
}
```

That's the whole file. ~50 lines. Add as `DataClasses\TruckToolsStatusMapper.cs` in the OTR API project.

---

## 4. Step 3 — Five insertion points

Each section below shows: where the new code goes, what it looks like, and what to verify.

### Insertion Point #1 — TruckerToolsController.PostLoad

**Method:** `PostLoad([FromBody]Load load)` near the bottom of `Controllers\TruckerToolsController.cs`.

**Where to insert:** inside the inner `try` block, **after** the line `dtt.InsertLoadResponse(response);`, before the `catch`.

#### Current code

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

#### Add after `dtt.InsertLoadResponse(response);`

```csharp
            dtt.InsertLoadResponse(response);

            // ─── NEW: dispatch LoadCreatedEvent to vendor framework ─────
            DispatchLoadCreated(load);
            // ────────────────────────────────────────────────────────────
        }
```

#### And add this helper at the bottom of `TruckerToolsController` (just before the closing `}` of the class)

```csharp
        // ─── Vendor framework dispatch helpers ──────────────────────

        private static void DispatchLoadCreated(OTR_API.TruckerTools.Models.Load load)
        {
            if (load == null) return;

            Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                new Vendor.Common.Events.LoadCreatedEvent
                {
                    VectorLoadId  = load.VectorID.ToString(),
                    SourceSystem  = "OTR_API",
                    OccurredUtc   = DateTime.UtcNow,
                    Mode          = load.loadType,
                    EquipmentType = load.equipmentType,
                    Weight        = ParseDecimal(load.weight),
                    WeightUnit    = "LB",                        // OTR doesn't track unit; FK accepts LB default
                    Origin        = MapStopToOrigin(load.pickup),
                    Destination   = MapStopToDestination(load.delivery),
                    References    = new System.Collections.Generic.List<Vendor.Common.Events.ReferenceNumber>
                    {
                        new Vendor.Common.Events.ReferenceNumber { Type = "LoadNumber", Value = load.loadNumber },
                        new Vendor.Common.Events.ReferenceNumber { Type = "ExternalId", Value = load.externalId }
                    }
                });
        }

        private static Vendor.Common.Events.StopInfo MapStopToOrigin(OTR_API.TruckerTools.Models.Pickup stop)
            => MapStop(stop, Vendor.Common.Events.StopRole.Pickup, sequenceNumber: 1);

        private static Vendor.Common.Events.StopInfo MapStopToDestination(OTR_API.TruckerTools.Models.Delivery stop)
            => MapStop(stop, Vendor.Common.Events.StopRole.Delivery, sequenceNumber: 999);

        private static Vendor.Common.Events.StopInfo MapStop(
            OTR_API.TruckerTools.Models.Stop stop,
            Vendor.Common.Events.StopRole role,
            int sequenceNumber)
        {
            if (stop == null) return null;
            return new Vendor.Common.Events.StopInfo
            {
                SequenceNumber       = sequenceNumber,
                Role                 = role,
                AddressLine1         = stop.Address,
                City                 = stop.City,
                State                = stop.State,
                PostalCode           = stop.PostalCode,
                ScheduledArrivalUtc  = ParseTtDate(stop.ScheduledAtEarlyDateTime),
                ScheduledDepartureUtc = ParseTtDate(stop.ScheduledAtLateDateTime)
            };
        }

        private static decimal? ParseDecimal(string raw)
            => decimal.TryParse(raw, out var d) ? d : (decimal?)null;

        private static DateTime? ParseTtDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt) ? dt : (DateTime?)null;
        }
```

**What to verify:**
1. OTR API still compiles and the existing PostLoad behavior is unchanged
2. With `VendorDispatch.Enabled = false`: POST a test load → no row appears in `VendorAPI_FK.VendorOutboundTransactions`
3. With `Enabled = true`: same POST creates a `VendorOutboundTransactions` row with:
   - `EventTypeName = 'LoadCreatedEvent'`
   - `VendorName = 'FourKites'`
   - `Status = 'PENDING'` initially, then `'ACK'` once FK responds
   - `RequestPayload` contains the load number and stop addresses

---

### Insertion Point #2 — TruckerToolsTrackingController.TrackLoad

**Method:** `TrackLoad([FromBody]Load load)` near the top of `Controllers\TruckerToolsTrackingController.cs`.

**Where to insert:** inside the inner `try`, after `dtt.InsertLoadResponse(response);`.

#### Add after `dtt.InsertLoadResponse(response);`

```csharp
                        dtt.InsertLoadResponse(response);

                        // ─── NEW: dispatch LoadAssignedEvent ─────────────────────
                        DispatchLoadAssigned(load);
                        // ─────────────────────────────────────────────────────────
                    }
```

#### Helper at the bottom of the controller class

```csharp
        // ─── Vendor framework dispatch helpers ──────────────────────

        private static void DispatchLoadAssigned(OTR_API.TruckerTools.Models.Load load)
        {
            if (load == null) return;

            Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                new Vendor.Common.Events.LoadAssignedEvent
                {
                    VectorLoadId = load.VectorID.ToString(),
                    SourceSystem = "OTR_API",
                    OccurredUtc  = DateTime.UtcNow,
                    Carrier = load.carrier != null
                        ? new Vendor.Common.Events.CarrierInfo
                          {
                              Name      = load.carrier.companyName,
                              Scac      = load.carrier.scac,
                              McNumber  = load.carrier.mc,
                              DotNumber = load.carrier.dotNumber
                          }
                        : null,
                    Driver = load.driver != null
                        ? new Vendor.Common.Events.DriverInfo
                          {
                              Name  = load.driver.Name,
                              Phone = load.driver.Phone
                          }
                        : null,
                    Equipment = new Vendor.Common.Events.EquipmentInfo
                    {
                        TruckNumber   = load.tractorNumber,
                        TrailerNumber = load.trailerNumber
                    }
                });
        }
```

**What to verify:**
- POST to `/api/truckertoolstracking/TrackLoad` with carrier + driver populated
- `VendorOutboundTransactions` shows new row with `EventTypeName = 'LoadAssignedEvent'`
- FK web UI shows carrier and driver attached to the load

---

### Insertion Point #3 — TruckerToolsTrackingController.UpdateTrackLoad

Same controller. Same helper. The update endpoint dispatches the same `LoadAssignedEvent` because a reassignment is an assignment.

#### Add after `dtt.InsertLoadResponse(response);` inside UpdateTrackLoad

```csharp
                        dtt.InsertLoadResponse(response);

                        // ─── NEW: dispatch LoadAssignedEvent ─────────────────────
                        DispatchLoadAssigned(load);
                        // ─────────────────────────────────────────────────────────
                    }
```

Reuses the helper from Insertion Point #2.

**What to verify:** Same as #2 but using PUT/UpdateTrackLoad.

---

### Insertion Point #4 — TruckerToolsTrackingController.CancelLoadTracking

Same controller. Different event.

#### Add after `dtt.InsertLoadResponse(response);` inside CancelLoadTracking

```csharp
                        dtt.InsertLoadResponse(response);

                        // ─── NEW: dispatch LoadTrackingStoppedEvent ──────────────
                        DispatchTrackingStopped(load);
                        // ─────────────────────────────────────────────────────────
                    }
```

#### Helper

```csharp
        private static void DispatchTrackingStopped(OTR_API.TruckerTools.Models.Load load)
        {
            if (load == null) return;

            Vendor.Common.Dispatch.VendorDispatcher.Instance.Dispatch(
                new Vendor.Common.Events.LoadTrackingStoppedEvent
                {
                    VectorLoadId = load.VectorID.ToString(),
                    SourceSystem = "OTR_API",
                    OccurredUtc  = DateTime.UtcNow,
                    Reason       = "DISPATCHER_STOPPED"
                });
        }
```

**What to verify:**
- POST to `/api/truckertoolstracking/CancelLoadTracking`
- `VendorOutboundTransactions` row with `EventTypeName = 'LoadTrackingStoppedEvent'`
- FK marks the load as no-longer-tracking

---

### Insertion Point #5 — TruckerToolsTrackingController.SendStatus ⚠️ HIGH VOLUME

**This is the busiest endpoint.** TruckTools webhooks here every 15 minutes per active load. Fire-and-forget dispatch is critical here — don't slow down the response to TT.

The existing method is ~150 lines. The dispatch goes near the end, **before** the final `return response;`.

#### Where to insert

Find this section (around the end of the method):

```csharp
                    try
                    {
                        int responseID = dtt.InsertLoadTrackingStatusResponse(response);
                        // ...commented-out email code...
                    }
                    catch(Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse");
                    }


                    if(!response.status)
                    {

                    }
```

#### Modify to add dispatch between the catch and the empty if

```csharp
                    try
                    {
                        int responseID = dtt.InsertLoadTrackingStatusResponse(response);
                        // ...commented-out email code...
                    }
                    catch(Exception ex)
                    {
                        da.InsertErrorAuditLog(ex.Message, "TrackLoadStatusResponse");
                    }

                    // ─── NEW: dispatch to vendor framework ──────────────
                    if (response.status)
                    {
                        DispatchStatusUpdate(lc);
                    }
                    // ────────────────────────────────────────────────────

                    if(!response.status)
                    {

                    }
```

#### Helper

```csharp
        private static void DispatchStatusUpdate(OTR_API.TruckerToolsTracking.Models.StatusUpdate lc)
        {
            if (lc == null || string.IsNullOrEmpty(lc.loadNumber)) return;

            var dispatcher = Vendor.Common.Dispatch.VendorDispatcher.Instance;
            var vectorLoadId = lc.loadNumber;

            // 1. Location event if any location data present
            var loc = lc.latestLocation
                   ?? lc.latestStatus?.location
                   ?? lc.status?.location;

            if (loc != null
                && !string.IsNullOrEmpty(loc.lat)
                && !string.IsNullOrEmpty(loc.lon))
            {
                dispatcher.Dispatch(new Vendor.Common.Events.LocationReportedEvent
                {
                    VectorLoadId = vectorLoadId,
                    SourceSystem = "OTR_API",
                    OccurredUtc  = DateTime.UtcNow,
                    Latitude     = loc.lat,
                    Longitude    = loc.lon,
                    LocatedAtUtc = ParseTtTimestamp(loc.timeStamp),
                    City         = loc.city,
                    State        = loc.state
                });
            }

            // 2. Status event if a status code is present
            var statusInfo = lc.latestStatus ?? lc.status;
            if (statusInfo != null && !string.IsNullOrEmpty(statusInfo.code))
            {
                var statusType = OTR_API.DataClasses.TruckToolsStatusMapper.Map(statusInfo.code);

                dispatcher.Dispatch(new Vendor.Common.Events.LoadStatusEvent
                {
                    VectorLoadId            = vectorLoadId,
                    SourceSystem            = "OTR_API",
                    OccurredUtc             = DateTime.UtcNow,
                    StatusType              = statusType,
                    SourceStatusCode        = statusInfo.code,
                    SourceStatusDescription = statusInfo.name,
                    StatusTimeUtc           = ParseTtTimestamp(statusInfo.timeStamp)
                });
            }
        }

        private static DateTime ParseTtTimestamp(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return DateTime.UtcNow;
            return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var dt) ? dt : DateTime.UtcNow;
        }
```

**What to verify:**
1. Response time of SendStatus stays within baseline (within 50ms) — fire-and-forget should add nothing visible
2. Existing TT-side persistence unchanged
3. Each call to SendStatus produces 1-2 `VendorOutboundTransactions` rows (location and/or status) within ~5 seconds
4. The FK row's `RequestPayload` shows the EDI 214 code translated from the TT status code (e.g., TT `"ARRIVED_PICKUP"` → FK `"X1"`)
5. Push 50+ updates in rapid succession; FK adapter throttles cleanly at its configured rate without backpressure into OTR

---

## 5. Recommended merge order

Stage these so each is validated before the next:

1. **Infrastructure first** — add project references, `TruckToolsStatusMapper.cs`, Web.config changes, Global.asax startup hook. **Leave `VendorDispatch.Enabled = false`.** Deploy and verify nothing broke.
2. **Verify ClientProfile seed row** exists in `VendorAPI_FK.ClientProfiles` (it does — we created it during smoke testing).
3. **Flip `VendorDispatch.Enabled = true` in dev.**
4. **Merge Insertion Point #2 (TrackLoad).** Lowest risk; easy to verify in isolation.
5. **Merge #3 (UpdateTrackLoad).** Reuses #2's helper.
6. **Merge #4 (CancelLoadTracking).** Same volume profile.
7. **Merge #1 (PostLoad).** Adds the helper class for stop mapping — slightly more code than the others.
8. **Merge #5 (SendStatus).** Highest volume; do last. Soak-test for 24 hours.

After each step: deploy, hit the affected endpoint, watch `VendorAPI_FK.VendorOutboundTransactions` for the expected row.

---

## 6. The grep test

After all merges, in the OTR API source folder:

```cmd
findstr /s /i /c:"fourkites" *.cs *.config *.csproj
```

Expected output: only matches in `OTR API.csproj` (the `Vendor.FourKites` ProjectReference) and Web.config (the `Vendor.FourKites` type name in `vendorAdapters`). **Zero matches in any `.cs` file.**

That's the proof the framework boundary holds.

---

## 7. Done-when checklist

- [ ] Project references to `Vendor.Common` and `Vendor.FourKites` added to OTR API
- [ ] `TruckToolsStatusMapper.cs` added under `DataClasses\`
- [ ] Web.config: 4 appSetting keys + section declaration + `<vendorAdapters>` section added
- [ ] Global.asax.cs: `Application_Start` calls `VendorDispatcher.Configure(...)`
- [ ] All 5 insertion points merged with their helper methods
- [ ] OTR API compiles with zero warnings about missing types
- [ ] `grep -i "fourkites"` in OTR API `.cs` files returns zero hits
- [ ] With `VendorDispatch.Enabled = false`: regression test confirms no behavior change
- [ ] With `Enabled = true`: each endpoint produces the expected `VendorOutboundTransactions` row
- [ ] 24-hour soak in staging shows expected volume and no errors

---

## 8. Open items still to resolve

| ID | Item | Blocks |
|---|---|---|
| O-001 | FK webhook auth scheme — confirm with CSM | Inbound (Deliverable #5) |
| O-002 | Refine `TruckToolsStatusMapper._byCode` after 90-day audit query | Quality of EDI 214 mapping; ship works in interim |
| O-501 | Vector Load table location for `LoadShipperResolver` | Multi-shipper routing; Phase 2 |
| O-701 | Cross-reference write strategy (Option A vs B) | Webhook controller (Deliverable #5) |

---

*End of OTR API Insertion Points (v3, ready-to-execute).*
