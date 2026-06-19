# Phase 1 Host Integration Tests

Test payloads and verification queries for the four insertion points wired into OTR API.

## What's here

| File | Purpose |
|---|---|
| `01_DispatcherStatus.http` | GET diagnostic endpoint -- confirms framework state |
| `02_TrackLoad.http` | POST sample TrackLoad -> expects LoadAssignedEvent |
| `03_UpdateTrackLoad.http` | POST sample UpdateTrackLoad -> expects LoadAssignedEvent (idempotent re-fire) |
| `04_CancelLoadTracking.http` | POST sample CancelLoadTracking -> expects LoadTrackingStoppedEvent |
| `05_SendStatus_Generous.http` | POST SendStatus in Generous mode -> expects 7 events |
| `06_SendStatus_Conservative.http` | Same payload after flipping verbosity -> expects 2 events |
| `07_RefreshMappings.http` | POST admin endpoint to flush mapping cache without restart |
| `99_VerifySQL.sql` | Verification queries (two servers: LocalDB + SQLDEVELOP) |

## How to run

Open any `.http` file in **VS Code with the REST Client extension** or **JetBrains Rider** -- both let you click "Send Request" inline. Or use the `curl` block at the bottom of each file from a Windows cmd window.

## Pre-flight

1. **OTR API running.** Set OTR API as startup project, F5 from VS. IIS Express opens on `http://localhost:5129`.
2. **`VendorDispatch.Enabled = "true"`** in `_OTR_API\Web.config`.
3. **HMAC auth** is enabled by default on the TT endpoints. For local testing, supply valid HMAC headers OR temporarily comment `[HMACAuthentication]` at the top of `TruckerToolsTrackingController.cs`. The admin and refresh endpoints have no auth (intended for local-only use; add auth before exposing).
4. **`spTrackingByLoadID_Get`** must exist in the `VectorOTR_TT` DB. It's needed by SendStatus dispatch to resolve `loadNumber` -> `VectorID`. Definition is in `_deliverables/07_SQL_Schema/scripts/`.

## Test data convention

| VectorID | loadNumber | Used by |
|---|---|---|
| 999001 | "999001" | TrackLoad, UpdateTrackLoad, SendStatus (Generous + Conservative) |
| 999002 | "999002" | CancelLoadTracking |

**Tracking SP varchar(10) constraint:** `loadNumber` and `loadTrackExternalId` columns are `varchar(10)`. Production convention is `loadNumber == VectorID` (e.g. `"342760"`). Don't use long test IDs like "VVI-TEST-001" -- they overflow and silently truncate or fail.

**Required fields the SPs need:** several stored procs rely on `AddWithValue(...)` which silently drops null parameters. Test payloads must include `loadTrackExternalId`, `dispatcherId`, `ltExternalId`, `driverPhone` for the relevant inserts to succeed.

Cleanup after testing:
```sql
DELETE FROM VendorAPI_FK.dbo.VendorOutboundTransactions WHERE VectorLoadId IN ('999001','999002');
```

(Tracking rows in `VectorOTR_TT` can be left -- production data is mixed in.)

## Expected results (VendorDispatch.Enabled=true)

| Test | Rows in VendorOutboundTransactions | Status |
|---|---|---|
| 02 TrackLoad | 1 LoadAssignedEvent for 999001 | HTTP_FAIL (placeholder FK URL) |
| 03 UpdateTrackLoad | +1 LoadAssignedEvent for 999001 | HTTP_FAIL |
| 04 CancelLoadTracking | 1 LoadTrackingStoppedEvent for 999002 | HTTP_FAIL |
| 05 SendStatus Generous | 7 mixed events for 999001 (5 Location + 2 Status) | HTTP_FAIL |
| 06 SendStatus Conservative | 2 events for 999001 (1 Location + 1 Status) | HTTP_FAIL |

**HTTP_FAIL is the expected Phase 1 result** -- the placeholder FK URL returns 404. What matters is the row's existence + correct EventTypeName + correct enriched RequestPayload. SUCCESS appears only when real FK sandbox creds are wired up.

With `VendorDispatch.Enabled = "false"`: zero rows for any test. OTR API responses unchanged.

## Where the audit log lives

Two separate audit logs across two servers:

- **Vendor check call audit** -> `(localdb)\mssqllocaldb` -> `VendorAPI_FK.dbo.VendorOutboundTransactions`. Every check-call attempt lands here whether it succeeds or fails.
- **OTR API's own error log** -> `DESKTOP-4DEA4AP\SQLDEVELOP` -> `VectorOTR.dbo.AuditLogs`. Anything thrown inside the framework's try/catch lands here via `DataAudit.InsertErrorAuditLog`. Look for `LogMessage LIKE 'TrackLoad.VendorDispatch%'` etc. (`LogTypeName = 'Error'` with source-prefixed message.)

`99_VerifySQL.sql` has blocks for both servers, clearly labeled.

## Quick workflow

```
1. Hit 01_DispatcherStatus       -> confirm READY
2. Hit 02_TrackLoad              -> run query #1   (1 LoadAssigned for 999001)
3. Hit 03_UpdateTrackLoad        -> run query #2   (>= 2 LoadAssigned for 999001)
4. Hit 04_CancelLoadTracking     -> run query #3   (1 LoadTrackingStopped for 999002)
5. Hit 05_SendStatus_Generous    -> run query #4   (7 events for 999001)
6. Flip verbosity to Conservative + restart OTR API
7. Hit 06_SendStatus_Conservative -> run query #5  (2 events for 999001)
8. Flip verbosity back to Generous
```

For mapping-override testing see comments in `07_RefreshMappings.http`.

## Cross-cutting notes

- The framework caches `VendorStatusMapping` rows. After any UPDATE/INSERT/DELETE on that table, POST to `/api/admin/refresh-mappings` to hot-reload without restarting OTR API. Same does NOT apply to ClientProfile changes (verbosity flips, etc.) -- those need a restart.
- HMAC validation is currently enabled on the TT endpoints. The four sample curls in each file use no auth; either disable HMAC for testing or set up signed requests.
