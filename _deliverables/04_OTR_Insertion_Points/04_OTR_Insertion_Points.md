# OTR API Integration Insertion Points — Phase 1

> **⚠️ SUPERSEDED — see D-025 in Master Strategy.**
>
> This document was written FourKites-direct: OTR API insertion points call a `FourKitesTeeOff` wrapper class directly. After review, Glen and Claude agreed this contradicted D-020 (framework-first build for resale). The rewrite calls `VendorDispatcher` instead, with FourKites as one of several pluggable adapters. See Deliverable #10 (`_deliverables\10_Framework_Design\10_Framework_Design.md`) for the framework design that the rewrite is based on.
>
> **This file is retained for historical reference and to show the contrast between FK-direct and framework-first approaches. Do not implement against this document. Wait for the rewritten version.**
>
> What's still useful in this document for the rewrite:
> - The five insertion-point locations in OTR API (PostLoad, TrackLoad, UpdateTrackLoad, SendStatus, CancelLoadTracking) — same five places
> - The tee-off pattern (additive, fail-safe, fire-and-forget) — same pattern, different call target
> - Fire-and-forget recommendations per endpoint — same
> - Pre-merge checklist concepts — same
>
> What changes in the rewrite:
> - `FourKitesTeeOff.Instance.SomeMethod(load)` → `VendorDispatcher.Instance.Dispatch(new SomeEvent { ... })`
> - `TruckToolsToEdi214Mapper` moves OUT of OTR API into `Vendor.FourKites.Adapter`
> - OTR API's Web.config additions become `VendorDispatch.*` keys (vendor-agnostic) instead of `FourKites.*` keys
> - A new `FourKitesTeeOff.cs` file is NOT added to OTR API — the framework handles that

---

# Original document (superseded) follows below.

# OTR API Integration Insertion Points — Phase 1

**Document:** Deliverable #4 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** Deliverables #2 (refactor) and #3 (OTR upgrade) complete
**Related decisions:** D-005, D-008, D-014, D-016, D-019

---

## 0. Purpose

This document shows **every place in OTR API where code must be added to tee off events to FourKites**. For each insertion point, it presents:

1. The existing code (excerpted from the actual file)
2. The proposed new code
3. A side-by-side comparison showing exactly what changes
4. The rationale
5. What to test after applying

**Glen executes the merges. Claude does not modify OTR API source.** This document is the merge guide.

**Cardinal pattern:** *Tee-off, fail-safe, additive.* New FourKites code runs AFTER existing operations succeed. If the FourKites call fails, the existing flow has already returned its normal response. FourKites failures are logged but **never break existing Vector or TruckTools traffic**.

---

## 1. Architectural pattern

### 1.1 The tee-off pattern

Every insertion point follows this shape:

```csharp
// EXISTING — unchanged
try {
    // existing operation (save to DB, call TruckTools, etc.)
    int Id = dtt.InsertSomething(thing);

    try {
        // existing operation (call out to TT, save response)
        response = wc.PostTTSomething(thing);
        dtt.InsertResponse(response);
    } catch (Exception ex) { /* existing error handling */ }

    // ─── NEW: tee-off to FourKites ──────────────────────────────
    // After the existing operation succeeds, fire-and-log the FK call.
    // Failures here NEVER affect the response returned to TT or FBS.
    try {
        _fkTeeOff.AssignmentForLoadAsync(thing);
    } catch (Exception fkEx) {
        da.InsertErrorAuditLog(fkEx.Message, "FourKitesTeeOff_TrackLoad");
    }
    // ────────────────────────────────────────────────────────────
}
catch (Exception ex) { /* existing error handling */ }
```

Three guarantees:

1. **Existing behavior is preserved.** The FK tee-off can be commented out and OTR API works exactly as it does today.
2. **FK failures don't propagate.** Wrapped in its own try/catch with audit logging.
3. **Errors get visibility.** Logged via the existing `DataAudit.InsertErrorAuditLog` pattern. The `Vendor.Common.OutboundTransactionRepository` also records every attempt to `VendorOutboundTransactions` regardless of success/failure, so we have a second layer of audit.

### 1.2 The wrapper class — `FourKitesTeeOff`

Rather than instantiating `Vendor.FourKites.FourKitesClient` directly in every controller, OTR API gets one new helper class — **`FourKitesTeeOff`** — that:

- Reads config (API key, shipper code, base URL) from `Web.config`
- Maps OTR API's existing `Load` and `StatusUpdate` models into FourKites DTOs
- Calls the appropriate FourKites client method
- Returns void (or `Task`) — controllers don't need to handle a FourKites response synchronously

This keeps the controllers minimal. Each insertion point is **one method call** rather than 30 lines of mapping logic.

This is also where the **TruckTools status code → EDI 214 mapping** lives (a method on the wrapper) — see Section 7.

**Location:** `OTR API\DataClasses\FourKitesTeeOff.cs` (new file). Goes in `DataClasses\` because that's where helper classes live in this codebase.

### 1.3 Synchronous vs. asynchronous

Existing OTR API code uses `Task.Run(() => ...).Result` — sync-over-async. New FK tee-off code matches this style to fit in. **It does not introduce async/await into existing methods**, which would require signature changes that ripple out.

Optionally, the tee-off can be **fire-and-forget** (`Task.Run` without `.Result`) so the FK call doesn't add latency to the HTTP response back to TruckTools/FBS. This is recommended for `SendStatus` specifically because TruckTools' webhook expects a fast response. See Section 6 for the recommendation per insertion point.

### 1.4 Where the new project reference goes

OTR API's `.csproj` (after Deliverable #3 upgrade) needs:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\FourKitesIntegration\fourkites-code\Vendor.FourKites\Vendor.FourKites.csproj">
    <Project>{your-vendor-fourkites-guid}</Project>
    <Name>Vendor.FourKites</Name>
  </ProjectReference>
  <ProjectReference Include="..\..\FourKitesIntegration\fourkites-code\Vendor.Common\Vendor.Common.csproj">
    <Project>{your-vendor-common-guid}</Project>
    <Name>Vendor.Common</Name>
  </ProjectReference>
</ItemGroup>
```

Or via Solution Explorer: Right-click OTR API project → Add → Reference → Project tab → check `Vendor.FourKites` and `Vendor.Common`.

For this to work, the OTR API project must be **in the same Visual Studio solution** as the Vendor.* projects, OR you can reference the built DLLs from a known shared location. **Recommendation: keep them in separate solutions for now** (OTR API has its own .sln) and reference the built DLLs:

```xml
<Reference Include="Vendor.FourKites">
  <HintPath>..\..\FourKitesIntegration\fourkites-code\Vendor.FourKites\bin\Release\Vendor.FourKites.dll</HintPath>
  <Private>True</Private>
</Reference>
<Reference Include="Vendor.Common">
  <HintPath>..\..\FourKitesIntegration\fourkites-code\Vendor.Common\bin\Release\Vendor.Common.dll</HintPath>
  <Private>True</Private>
</Reference>
```

The DLL-reference approach is more conservative — Vendor.* and OTR API can rev independently — and you can switch to project references later if you decide to consolidate solutions.

---

## 2. Configuration changes — Web.config

Add these to `Web.config` in the `<appSettings>` section.

```xml
<appSettings>
  <!-- existing settings unchanged -->

  <!-- FourKites tee-off settings (new) -->
  <add key="FourKites.Enabled" value="true" />
  <add key="FourKites.BaseUrl" value="https://api.fourkites.com" />
  <add key="FourKites.ApiKey" value="YOUR-API-KEY-HERE" />
  <add key="FourKites.BillToCode" value="VECTOR-BILLTO-CODE" />
  <add key="FourKites.SourceIdentifier" value="OTR_API" />
  <add key="FourKites.AuditConnectionString" value="Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True" />
  <add key="FourKites.FireAndForget" value="true" />
  <add key="FourKites.TimeoutSeconds" value="15" />
</appSettings>
```

**Key meanings:**

| Setting | Purpose |
|---|---|
| `FourKites.Enabled` | Master kill switch. `false` disables ALL tee-offs without code changes. Useful for emergency rollback. |
| `FourKites.BaseUrl` | Production: `https://api.fourkites.com`. Sandbox: per Glen's FK contract. |
| `FourKites.ApiKey` | FourKites API key. Get from Glen / FK CSM. **Production keys should be stored encrypted or in DPAPI, not plaintext** — see Open Item O-007. |
| `FourKites.BillToCode` | Vector's BillToCode in FourKites' system (per shipper if multi-shipper later). |
| `FourKites.SourceIdentifier` | Tagged on every transaction row to identify OTR API as the caller (vs FBS or VB.NET app). |
| `FourKites.AuditConnectionString` | Connection to `VendorAPI_FK` database for `Vendor.Common` repositories. |
| `FourKites.FireAndForget` | If true, tee-off calls run on a thread pool task without `.Result` (no latency added to OTR API response). If false, controllers wait for FK call to complete. **Recommended: true for SendStatus, false elsewhere.** |
| `FourKites.TimeoutSeconds` | Per-call timeout. Default 15s. |

**Connection string note:** The `VendorAPI_FK` database is on the existing SQL Express server. Schema is defined in Deliverable #7. The connection string can use Integrated Security (Windows auth from the IIS app pool identity) or SQL auth — match what your other OTR API connection strings use.

---

## 3. The wrapper class — `FourKitesTeeOff.cs`

This new file goes in `OTR API\DataClasses\FourKitesTeeOff.cs`. It is the **single point of contact** between OTR API and Vendor.FourKites. Every controller insertion point is one method call on this class.

The class is shown here in full (~200 lines). It handles:
- Configuration loading
- Client lifecycle (singleton-style; one `FourKitesClient` per app instance)
- Mapping from OTR API's models to FourKites DTOs
- Fire-and-forget execution
- Error logging via existing `DataAudit`

```csharp
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using OTR_API.TruckerTools.Models;
using OTR_API.TruckerToolsTracking.Models;
using Vendor.Common.Persistence;
using Vendor.FourKites.Client;
using Vendor.FourKites.Mapping;
using Vendor.FourKites.Models.Common;
using Vendor.FourKites.Models.CreateShipment;
using Vendor.FourKites.Models.DispatcherUpdate;

namespace OTR_API.DataClasses
{
    /// <summary>
    /// Thin wrapper that adapts OTR API's existing Load/StatusUpdate models to FourKites DTOs
    /// and fires API calls. Every controller insertion point goes through this class.
    ///
    /// Errors are logged via DataAudit. Failures NEVER propagate to controllers — callers
    /// can safely call these methods knowing nothing will throw out.
    /// </summary>
    public class FourKitesTeeOff
    {
        private static readonly Lazy<FourKitesTeeOff> _instance =
            new Lazy<FourKitesTeeOff>(() => new FourKitesTeeOff());

        /// <summary>Process-wide singleton. Use FourKitesTeeOff.Instance from controllers.</summary>
        public static FourKitesTeeOff Instance => _instance.Value;

        private readonly FourKitesClient _client;
        private readonly OutboundTransactionRepository _audit;
        private readonly string _billToCode;
        private readonly string _sourceIdentifier;
        private readonly bool _enabled;
        private readonly bool _fireAndForget;

        private FourKitesTeeOff()
        {
            _enabled = bool.Parse(ConfigurationManager.AppSettings["FourKites.Enabled"] ?? "false");
            if (!_enabled) return;

            var options = new FourKitesClientOptions
            {
                BaseUrl = ConfigurationManager.AppSettings["FourKites.BaseUrl"],
                ApiKey  = ConfigurationManager.AppSettings["FourKites.ApiKey"],
                TimeoutSeconds = int.Parse(
                    ConfigurationManager.AppSettings["FourKites.TimeoutSeconds"] ?? "15")
            };
            _billToCode       = ConfigurationManager.AppSettings["FourKites.BillToCode"];
            _sourceIdentifier = ConfigurationManager.AppSettings["FourKites.SourceIdentifier"] ?? "OTR_API";
            _fireAndForget    = bool.Parse(
                ConfigurationManager.AppSettings["FourKites.FireAndForget"] ?? "true");
            _audit = new OutboundTransactionRepository(
                ConfigurationManager.AppSettings["FourKites.AuditConnectionString"]);
            _client = new FourKitesClient(options, _audit);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Insertion Point #1 — PostLoad (TruckerToolsController)
        // ─────────────────────────────────────────────────────────────────────────
        public void CreateShipmentForLoad(Load load)
        {
            if (!_enabled || load == null) return;
            FireSafely(() => CreateShipmentForLoadInternal(load), "CreateShipment");
        }

        private async Task CreateShipmentForLoadInternal(Load load)
        {
            var request = new CreateShipmentRequest
            {
                BillToCode    = _billToCode,
                LoadNumber    = load.VectorID.ToString(),
                IdentifierKeys = new List<IdentifierKey>
                {
                    new IdentifierKey { Identifier = load.VectorID.ToString(), IdentifierType = "loadNumber" }
                },
                // TODO: populate stops, mode, equipment from load.* properties
                // See Open Item O-201 — full Load → CreateShipment mapping.
                SourceSystem = _sourceIdentifier,
                VectorLoadId = load.VectorID.ToString()
            };
            await _client.CreateShipmentAsync(request).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Insertion Point #2 — TrackLoad / UpdateTrackLoad (TruckerToolsTrackingController)
        // ─────────────────────────────────────────────────────────────────────────
        public void AssignmentForLoad(Load load)
        {
            if (!_enabled || load == null) return;
            FireSafely(() => AssignmentForLoadInternal(load), "AssignmentUpdate");
        }

        private async Task AssignmentForLoadInternal(Load load)
        {
            var batch = new DispatcherBatch
            {
                BillToCode = _billToCode,
                LoadUpdate = new List<LoadUpdate>
                {
                    new LoadUpdate
                    {
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = load.VectorID.ToString(), IdentifierType = "loadNumber" }
                        },
                        AssignmentUpdate = new AssignmentUpdate
                        {
                            CarrierScac     = load.carrierSCAC,    // confirm property name in OTR Load model
                            TruckNumber     = load.truckNumber,
                            TrailerNumber   = load.trailerNumber,
                            DriverName      = load.driverName,
                            DriverPhone     = load.driverCell
                        }
                    }
                },
                SourceSystem = _sourceIdentifier,
                VectorLoadId = load.VectorID.ToString()
            };
            await _client.SendDispatcherUpdateAsync(batch).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Insertion Point #3 — SendStatus (TruckerToolsTrackingController) — THE BIG ONE
        // Fires location update + event update in a single batch.
        // ─────────────────────────────────────────────────────────────────────────
        public void StatusAndLocationForUpdate(StatusUpdate lc)
        {
            if (!_enabled || lc == null) return;
            FireSafely(() => StatusAndLocationForUpdateInternal(lc), "DispatcherUpdate_Status");
        }

        private async Task StatusAndLocationForUpdateInternal(StatusUpdate lc)
        {
            var loadUpdate = new LoadUpdate
            {
                IdentifierKeys = new List<IdentifierKey>
                {
                    new IdentifierKey { Identifier = lc.loadNumber, IdentifierType = "loadNumber" }
                }
            };

            // Location: prefer latestLocation; if absent, use latestStatus.location
            var loc = lc.latestLocation
                   ?? lc.latestStatus?.location
                   ?? lc.status?.location;
            if (loc != null && !string.IsNullOrEmpty(loc.lat) && !string.IsNullOrEmpty(loc.lon))
            {
                loadUpdate.LocationUpdate = new LocationUpdate
                {
                    Latitude   = loc.lat,
                    Longitude  = loc.lon,
                    LocatedAt  = FourKitesTime.ParseAndFormat(loc.timeStamp),
                    City       = loc.city,
                    State      = loc.state
                };
            }

            // Event: from latestStatus, mapped via EDI 214 mapper
            var statusInfo = lc.latestStatus ?? lc.status;
            if (statusInfo != null && !string.IsNullOrEmpty(statusInfo.code))
            {
                var ediCode = TruckToolsToEdi214Mapper.Map(statusInfo.code);
                if (ediCode != null)  // null means "unmapped status, skip event update"
                {
                    loadUpdate.EventUpdate = new EventUpdate
                    {
                        StatusCode         = ediCode,
                        StatusDescription  = statusInfo.name,
                        EventTimestamp     = FourKitesTime.ParseAndFormat(statusInfo.timeStamp),
                        City               = statusInfo.location?.city,
                        State              = statusInfo.location?.state
                    };
                }
            }

            if (loadUpdate.LocationUpdate == null && loadUpdate.EventUpdate == null)
                return;  // nothing to send

            var batch = new DispatcherBatch
            {
                BillToCode = _billToCode,
                LoadUpdate = new List<LoadUpdate> { loadUpdate },
                SourceSystem = _sourceIdentifier,
                VectorLoadId = lc.loadNumber
            };
            await _client.SendDispatcherUpdateAsync(batch).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Insertion Point #4 — CancelLoadTracking (TruckerToolsTrackingController)
        // ─────────────────────────────────────────────────────────────────────────
        public void StopTrackingForLoad(Load load)
        {
            if (!_enabled || load == null) return;
            FireSafely(() => StopTrackingForLoadInternal(load), "LoadInfoUpdate_StopTracking");
        }

        private async Task StopTrackingForLoadInternal(Load load)
        {
            var batch = new DispatcherBatch
            {
                BillToCode = _billToCode,
                LoadUpdate = new List<LoadUpdate>
                {
                    new LoadUpdate
                    {
                        IdentifierKeys = new List<IdentifierKey>
                        {
                            new IdentifierKey { Identifier = load.VectorID.ToString(), IdentifierType = "loadNumber" }
                        },
                        LoadInfoUpdate = new LoadInfoUpdate { StopTracking = true }
                    }
                },
                SourceSystem = _sourceIdentifier,
                VectorLoadId = load.VectorID.ToString()
            };
            await _client.SendDispatcherUpdateAsync(batch).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Shared error handling
        // ─────────────────────────────────────────────────────────────────────────
        private void FireSafely(Func<Task> action, string operationName)
        {
            try
            {
                if (_fireAndForget)
                {
                    // Fire-and-forget: don't add latency to OTR API response
                    Task.Run(async () =>
                    {
                        try { await action().ConfigureAwait(false); }
                        catch (Exception ex) { LogError(ex, operationName); }
                    });
                }
                else
                {
                    // Sync-wait: matches existing OTR style; surfaces FK errors faster
                    Task.Run(action).Wait();
                }
            }
            catch (Exception ex)
            {
                LogError(ex, operationName);
            }
        }

        private void LogError(Exception ex, string operationName)
        {
            try
            {
                var da = new DataAudit();
                da.InsertErrorAuditLog(ex.ToString(), "FourKitesTeeOff_" + operationName);
            }
            catch { /* If audit logging itself fails, swallow — never break the caller */ }
        }
    }
}
```

**Notes on this code:**

1. **Singleton pattern via Lazy.** One FK client per app instance, lazy-initialized on first use. Cheap if FK is disabled (constructor early-returns).
2. **All public methods are void.** No way to fail the caller. Errors go to the audit log.
3. **Property names assumed.** `load.VectorID`, `load.truckNumber`, `load.carrierSCAC`, etc. Glen verifies actual property names from the `Load` model and adjusts.
4. **Mapping is intentionally minimal.** Full Create Shipment mapping (stops, equipment, weight, etc.) is Open Item O-201 — needs business analysis of which load fields populate which FK fields. The stub gets the integration wired; richer mapping can be added incrementally.
5. **TruckToolsToEdi214Mapper.Map()** — referenced but not defined here. See Section 7.

---

## 4. Insertion points — side-by-side merges

For each of the four insertion points below, the diff shows:
- **Left:** existing code (what's there now)
- **Right:** new code (what to merge in)

### 4.1 Insertion Point #1 — `TruckerToolsController.PostLoad`

**File:** `Controllers\TruckerToolsController.cs`
**Existing line range:** ~270–305
**What this endpoint does:** Receives a matched-trip Load from Vector FBS; saves it locally; forwards to TruckTools.
**Tee-off action:** Tell FourKites a new shipment exists.

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

            // ─── NEW: tee-off to FourKites ─────────────────────
            FourKitesTeeOff.Instance.CreateShipmentForLoad(load);
            // ───────────────────────────────────────────────────
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

**Why this placement:** The FK tee-off goes *inside* the inner try, *after* `dtt.InsertLoadResponse(response)`. This means we only tell FK about loads we successfully posted to TruckTools. If the TT post fails, we don't create a phantom shipment in FK.

**What to test:**
1. POST a test load to `/api/truckertools/postload` → returns 200 with `LoadID` populated
2. Verify load row in `VendorOTR_TT` (existing behavior)
3. Verify TruckTools received the load (existing behavior)
4. Verify new row in `VendorAPI_FK.VendorOutboundTransactions` with `UpdateType = 'createShipment'`, `VendorName = 'FourKites'`, `Status` progressing to `ACK`
5. With `FourKites.Enabled = false` in Web.config: behave exactly like before, no FK row

---

### 4.2 Insertion Point #2 — `TruckerToolsTrackingController.TrackLoad`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~17–55
**What this endpoint does:** Tells TruckTools to start tracking a load (driver/truck/trailer info attached).
**Tee-off action:** Send AssignmentUpdate to FourKites.

#### Existing code

```csharp
[HttpPost]
public TrackingResponse TrackLoad([FromBody]Load load)
{
    TrackingResponse response = new TrackingResponse();

    OTR_API.DataClasses.DataAudit da = new OTR_API.DataClasses.DataAudit();
    if (load == null)
    {
        da.InsertErrorAuditLog("Body Message is null", "LoadTrack");
        response.response.Message = "Error Tracking Load";
    }
    else {
        DataTruckerToolsTracking dtt = new DataTruckerToolsTracking();
        try
        {
            load = dtt.AddLoadTrackingTimeZones(load);
            int LoadID = dtt.InsertLoadTracking(load);

            try
            {
                WebCallFunctions wc = new WebCallFunctions();
                Task<TrackingResponse> task1 = Task.Run(() => { return wc.PostTrackLoad(load); });
                response = task1.Result;
                response.response.TrackingID = LoadID;
                dtt.InsertLoadResponse(response);
            }
            catch (Exception ex)
            {
                response.response.Message = "Error Posting Load - " + ex.Message;
            }
        }
        catch (Exception ex)
        {
            da.InsertErrorAuditLog(ex.Message, "TrackLoad");
            response.response.Message = "Error Saving Load";
        }
    }
    return response;
}
```

#### Proposed code

Add ONE LINE after `dtt.InsertLoadResponse(response);`:

```csharp
// ─── NEW: tee-off to FourKites ─────────────────────
FourKitesTeeOff.Instance.AssignmentForLoad(load);
// ───────────────────────────────────────────────────
```

So the inner try block becomes:

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.PostTrackLoad(load); });
    response = task1.Result;
    response.response.TrackingID = LoadID;
    dtt.InsertLoadResponse(response);

    // ─── NEW: tee-off to FourKites ─────────────────────
    FourKitesTeeOff.Instance.AssignmentForLoad(load);
    // ───────────────────────────────────────────────────
}
catch (Exception ex)
{
    response.response.Message = "Error Posting Load - " + ex.Message;
}
```

**What to test:**
1. POST to `/api/truckertools/trackload` with a Load that has truck/trailer/driver populated
2. Verify TruckTools starts tracking (existing behavior)
3. Verify `VendorOutboundTransactions` row with `UpdateType = 'dispatcherUpdate'` and `RequestPayload` containing assignmentUpdate
4. Verify FourKites receives the AssignmentUpdate (check FK web UI for driver/truck appearing on the load)

---

### 4.3 Insertion Point #3 — `TruckerToolsTrackingController.UpdateTrackLoad`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~62–105
**What this endpoint does:** Updates tracking info for an already-tracking load (driver swap, truck change, etc.).
**Tee-off action:** Send AssignmentUpdate to FourKites (same shape as TrackLoad — FK doesn't distinguish initial vs. update).

#### Existing code

Same shape as `TrackLoad` above; uses `dtt.UpdateLoadTracking` and `wc.PutUpdateTrackLoad`.

#### Proposed code

Add the SAME line in the same relative position:

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.PutUpdateTrackLoad(load); });
    response = task1.Result;
    response.response.TrackingID = LoadID;
    dtt.InsertLoadResponse(response);

    // ─── NEW: tee-off to FourKites ─────────────────────
    FourKitesTeeOff.Instance.AssignmentForLoad(load);
    // ───────────────────────────────────────────────────
}
catch (Exception ex)
{
    response.response.Message = "Error Posting Load - " + ex.Message;
}
```

**Why the same call as TrackLoad:** FourKites' AssignmentUpdate is idempotent — sending the same driver/truck info twice is harmless. We don't need a separate "update" method.

**What to test:** Same tests as Insertion Point #2 but for updates. Confirm FK reflects the change.

---

### 4.4 Insertion Point #4 — `TruckerToolsTrackingController.SendStatus`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~152–310 (long method)
**What this endpoint does:** Receives a webhook from TruckTools with location + status data; saves multi-layered data to several tables; returns success/error response.
**Tee-off action:** Send LocationUpdate + EventUpdate to FourKites.

This is the **highest-volume insertion point** — fires every 15 minutes per active load. Must be performant. **Fire-and-forget is mandatory here.**

#### Existing code

The existing method is long (160 lines). The insertion point is **just before the final `return response;`** at the end of the method, OR just before the success branch returns. The exact line to add the tee-off is *after* all the data has been validated and persisted, but *before* returning to TruckTools.

#### Proposed code

Look for this section near the end of the method (around line 290–310, just before the catch blocks at the very end):

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

Add the tee-off **after the response is logged but before the final `return`**, only when status is true (i.e. validation passed):

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

                    // ─── NEW: tee-off to FourKites ─────────────────────
                    if (response.status)
                    {
                        FourKitesTeeOff.Instance.StatusAndLocationForUpdate(lc);
                    }
                    // ───────────────────────────────────────────────────

                    if(!response.status)
                    {

                    }
```

**Why this placement:**
- After data persistence — so the FK call has access to validated data and we've recorded what TT sent
- Inside the `try/catch (Exception ex)` that wraps the whole status processing — if it does throw (it won't because `StatusAndLocationForUpdate` is internally try/catch'd), it's caught
- Gated on `response.status` — don't push junk data to FK if validation failed
- Before the `return response;` at method bottom — TT still gets its normal response

**Recommended `FourKites.FireAndForget = true` in Web.config** for this endpoint specifically. TT expects fast webhook responses; we shouldn't make them wait for our FK call.

**What to test:**
1. Simulate or wait for a TT webhook → `/api/truckertools/sendstatus`
2. Verify response time stays similar to baseline (within 100ms)
3. Verify all existing TT-side persistence still happens (`VendorOTR_TT` rows)
4. Verify FK receives LocationUpdate within ~5 seconds (fire-and-forget)
5. Verify `VendorOutboundTransactions` row with `UpdateType = 'dispatcherUpdate'`, request payload contains locationUpdate (and eventUpdate if status code mapped)
6. Verify rate-limit behavior: with 50+ loads pushing every 15 min, the `Vendor.FourKites.RateLimitTracker` should throttle correctly

---

### 4.5 Insertion Point #5 — `TruckerToolsTrackingController.CancelLoadTracking`

**File:** `Controllers\TruckerToolsTrackingController.cs`
**Existing line range:** ~110–148
**What this endpoint does:** Stops TruckTools tracking on a load.
**Tee-off action:** Send LoadInfoUpdate with StopTracking=true to FourKites.

#### Existing code

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.CancelLoadTracking(load); });
    response = task1.Result;
    response.response.TrackingID = TrackingID;
    dtt.InsertLoadResponse(response);
}
catch (Exception ex)
{
    response.response.Message = "Error Cancelling Load - " + ex.Message;
}
```

#### Proposed code

Same one-line addition:

```csharp
try
{
    WebCallFunctions wc = new WebCallFunctions();
    Task<TrackingResponse> task1 = Task.Run(() => { return wc.CancelLoadTracking(load); });
    response = task1.Result;
    response.response.TrackingID = TrackingID;
    dtt.InsertLoadResponse(response);

    // ─── NEW: tee-off to FourKites ─────────────────────
    FourKitesTeeOff.Instance.StopTrackingForLoad(load);
    // ───────────────────────────────────────────────────
}
catch (Exception ex)
{
    response.response.Message = "Error Cancelling Load - " + ex.Message;
}
```

**What to test:**
1. POST to `/api/truckertools/cancelloadtracking` for an active load
2. Verify TruckTools stops tracking (existing behavior)
3. Verify FK shows the load as tracking-stopped
4. Verify `VendorOutboundTransactions` row with payload containing `loadInfoUpdate.stopTracking = true`

---

## 5. Summary of changes to OTR API

This is the complete list of files Glen touches:

| File | Change type | Lines added |
|---|---|---|
| `OTR API.csproj` | Add 2 references (Vendor.FourKites, Vendor.Common) | 6 lines |
| `Web.config` | Add 8 `<add key=>` entries to `<appSettings>` | 8 lines |
| `DataClasses\FourKitesTeeOff.cs` | **NEW FILE** | ~200 lines |
| `Controllers\TruckerToolsController.cs` | Add 3 lines inside PostLoad | 3 lines |
| `Controllers\TruckerToolsTrackingController.cs` | Add 3 lines × 4 places (TrackLoad, UpdateTrackLoad, SendStatus, CancelLoadTracking) | 12 lines |

**Total: ~230 lines added across 5 files. Zero lines removed. Zero existing logic modified.**

Every change is **additive**. Disabling `FourKites.Enabled = false` returns OTR API to identical pre-merge behavior.

---

## 6. Fire-and-forget recommendations per endpoint

| Endpoint | Recommended FireAndForget |
|---|---|
| PostLoad (CreateShipment) | **false** — low frequency, want synchronous confirmation |
| TrackLoad (AssignmentUpdate) | false — low frequency |
| UpdateTrackLoad (AssignmentUpdate) | false |
| **SendStatus (DispatcherUpdate, ~15-min cadence)** | **true** — high frequency, FK latency must not delay TT webhook ACK |
| CancelLoadTracking (StopTracking) | false |

The current `FourKitesTeeOff` design uses a single global `FireAndForget` config setting. If you want per-endpoint control, the wrapper can be refactored to take a per-method parameter — but recommend starting with the global flag set to `true` (safest) and refining if needed.

---

## 7. The TruckTools → EDI 214 status code mapping

This is the unresolved piece — Open Item O-002 in the strategy doc. Glen pulls real TT status codes from the OTR audit log.

The mapping itself is a **simple lookup table** that lives in `Vendor.FourKites\Mapping\Edi214Mapper.cs` (already exists from the original FourKitesIntegration.Core) plus a new lookup table for TT codes.

Proposed structure (in `OTR API\DataClasses\TruckToolsToEdi214Mapper.cs` — a new static class, lives in OTR API since it's TT-specific):

```csharp
using System.Collections.Generic;

namespace OTR_API.DataClasses
{
    /// <summary>
    /// Maps TruckTools status codes to FourKites EDI 214 codes.
    /// Returns null for unmapped codes — caller should skip the event update.
    /// </summary>
    public static class TruckToolsToEdi214Mapper
    {
        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // TODO: populate from audit log analysis (Open Item O-002)
            // Format: { "TT code", "EDI 214 code" }
            // Examples (verify these from real audit data before deploying):
            { "DISPATCHED",        "X1" },  // Dispatched
            { "ARRIVED_PICKUP",    "X3" },  // Arrived at pickup
            { "DEPARTED_PICKUP",   "AF" },  // Departed pickup
            { "ARRIVED_DELIVERY",  "X1" },  // Arrived at delivery
            { "DEPARTED_DELIVERY", "D1" },  // Completed delivery
            { "DELIVERED",         "D1" },  // Delivery complete
            // ... rows to be filled in from audit log per O-002
        };

        public static string Map(string truckToolsCode)
        {
            if (string.IsNullOrWhiteSpace(truckToolsCode)) return null;
            return _map.TryGetValue(truckToolsCode, out var ediCode) ? ediCode : null;
        }
    }
}
```

**How to populate:**

Run this query against `VectorOTR_TT` (or wherever the audit log lives):

```sql
SELECT DISTINCT
    statusCode,
    statusName,
    COUNT(*) AS occurrences,
    MIN(timeStamp) AS earliest,
    MAX(timeStamp) AS latest
FROM dbo.LoadTrackingStatusInfo  -- table name approximate; adjust to actual
WHERE statusCode IS NOT NULL
GROUP BY statusCode, statusName
ORDER BY occurrences DESC;
```

(Exact table name needs verification — Glen knows the schema.)

The result tells you which TT codes actually appear in production. Map each one to its EDI 214 equivalent. If a code is rare (< 0.1% of total) and ambiguous, mapping it to `null` (skip) is fine — it won't break anything, FK just won't see that event.

---

## 8. Merge order — recommended sequence

Don't merge all five insertion points at once. Stage them so each is validated before the next:

1. **Wire up infrastructure first.** Add the project/DLL references and `FourKitesTeeOff.cs` with `FourKites.Enabled = false`. Build and deploy. Verify nothing broke.
2. **Enable FK and merge Insertion Point #2 (TrackLoad).** Lowest volume; once-per-load. Easy to verify and roll back.
3. **Merge Insertion Point #3 (UpdateTrackLoad).** Same shape; trivial after #2 works.
4. **Merge Insertion Point #5 (CancelLoadTracking).** Same volume profile.
5. **Merge Insertion Point #1 (PostLoad / CreateShipment).** New territory (CreateShipment payload is richer); verify load fields map correctly.
6. **Merge Insertion Point #4 (SendStatus).** HIGHEST volume — leave for last after the others are proven. Fire-and-forget validation matters here.

After each step: deploy, run smoke tests, watch the audit log for 24 hours.

---

## 9. Pre-merge checklist

Before applying ANY merge:

- [ ] Deliverable #2 (refactor) is complete; Vendor.FourKites.dll exists
- [ ] Deliverable #3 (OTR API upgrade) is complete; OTR API targets .NET 4.8.1
- [ ] Deliverable #7 (SQL schema) is complete; `VendorAPI_FK` database exists
- [ ] FourKites API key obtained and saved to Web.config (sandbox to start)
- [ ] BillToCode confirmed with FourKites CSM
- [ ] TT status code → EDI 214 mapping populated (per Section 7)
- [ ] Vendor.FourKites SmokeTest passes against sandbox

---

## 10. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-201 | Full CreateShipment payload mapping (which Load fields populate which FK fields: stops, equipment, weight, references) | Insertion Point #1 going live; can be deployed with stub first |
| O-202 | Property name verification on OTR's Load model — does it expose `truckNumber`, `trailerNumber`, `driverName`, `driverCell`, `carrierSCAC`? Or are they named differently? | Building FourKitesTeeOff.cs |
| O-203 | Property name verification on `StatusUpdate.latestLocation.timeStamp` and `latestStatus.timeStamp` format — is FK's ISO 8601 parser tolerant of "MM/dd/yyyy HH:mm:ss tt K" or do we need to convert? | Insertion Point #4 going live |
| O-204 | Production API key delivery to OTR API server (config encryption, vault, environment variable?) | Production deployment |
| O-205 | Does TruckTools' webhook IP need to be allowlisted on FK's side? (Unlikely; we're calling out to FK, not the reverse.) | Should be no |

---

## 11. Done-when checklist

Mark this deliverable complete when:

- [ ] `FourKitesTeeOff.cs` added to OTR API and compiles
- [ ] `Web.config` updated with all 8 FourKites settings
- [ ] OTR API references Vendor.FourKites.dll and Vendor.Common.dll successfully
- [ ] All five insertion points merged
- [ ] With `FourKites.Enabled = false`: OTR API behaves identically to pre-merge (regression test)
- [ ] With `FourKites.Enabled = true`: each insertion point fires a `VendorOutboundTransactions` row when the corresponding TT/FBS endpoint is called
- [ ] Status code mapping (`TruckToolsToEdi214Mapper`) populated from audit log analysis
- [ ] 24-hour soak test in staging shows expected transaction volume and no errors above baseline
- [ ] Production deployed with `FireAndForget = true` for SendStatus, `false` for others
- [ ] Audit log shows successful (status = ACK) transactions for at least one of each insertion-point type

---

*End of OTR API Insertion Points document.*
