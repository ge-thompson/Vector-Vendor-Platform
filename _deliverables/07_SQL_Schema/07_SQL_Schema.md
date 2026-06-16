# VendorAPI_FK SQL Schema

**Document:** Deliverable #7 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (designer)
**Prerequisites:** Deliverable #10 (framework design) reviewed
**Related decisions:** D-008, D-009, D-018, D-020, D-024

---

## 0. Purpose

This deliverable produces the complete SQL schema for the `VendorAPI_FK` database — the audit / configuration / correlation database that every other deliverable references. It is purely structural: tables, indexes, constraints, and a few stored procedures.

After running these scripts:
- `VendorAPI_FK` database exists on `10.10.9.10\SQLEXPRESS12`
- All tables referenced by Deliverables #2, #4, #5, #8, and #10 are present
- One sample `ClientProfile` row exists (FourKites for `VECTOR_DEFAULT`)
- The "show me everything for VectorLoadId X" query (D-024) returns results

---

## 1. Database name and rationale

Per D-009: `VendorAPI_FK` lives on the existing SQL Express instance `10.10.9.10\SQLEXPRESS12`.

**The `_FK` suffix officially means "FrameworK", not FourKites** (per D-026). This was explicitly clarified during the build phase because the original ambiguity ("could mean either") was confusing. The database is vendor-agnostic by design:

- Every table has a `VendorName` column — no FourKites-specific columns or tables
- The audit log holds dispatches to ANY configured vendor; adding project44 next quarter requires zero schema changes
- The name does NOT need to change when vendor #2 ships

If you're reading this and were confused by `_FK`, that's the documented answer.

---

## 2. File inventory

The `scripts/` folder contains:

| # | File | What it does | Idempotent? |
|---|---|---|---|
| `01` | `00_CreateDatabase.sql` | Creates `VendorAPI_FK` database; sets options | Yes |
| `02` | `01_ClientProfiles.sql` | Configuration table for per-shipper, per-vendor routing | Yes |
| `03` | `02_VendorOutboundTransactions.sql` | Outbound audit log — every call we make to any vendor | Yes |
| `04` | `03_VendorInboundCallbacks.sql` | Inbound audit log — every webhook we receive | Yes |
| `05` | `04_LoadCrossReference.sql` | VectorLoadId ↔ VendorLoadId mapping (vendor-agnostic alternative to schema additions on Vector's Load table) | Yes |
| `06` | `05_VendorRateLimitWindow.sql` | Optional DB-backed rate limit state (for survival across app pool restarts) | Yes |
| `07` | `06_StoredProcedures.sql` | A few server-side queries that benefit from being procs (the "everything for a load" report, dedupe upsert) | Yes |
| `08` | `07_SeedData.sql` | Sample `ClientProfile` row for FK | Yes (uses MERGE) |
| `09` | `99_Verify.sql` | Read-only verification: tables exist, indexes exist, sample insert works | Yes — read-only |

**"Idempotent"** means: you can re-run the script multiple times without errors. Important for environment refreshes and for hand-merging schema changes back later.

---

## 3. Table-by-table design notes

### 3.1 `ClientProfiles` — the routing config

Defines which vendors get which events for which shippers. The `VendorDispatcher` reads this on every dispatch (with caching).

**Key shape:**
- `ShipperCode + VendorName` is unique — one row per shipper-vendor pair
- `EnabledEvents` is a CSV string of event type names (`"LoadCreatedEvent,LocationReportedEvent,..."`)
- `ConfigJson` is the vendor-specific blob (API key, base URL, billToCode for FK; OAuth credentials for project44; etc.)
- `IsActive` enables disabling a vendor without deleting the row

**Why CSV for EnabledEvents instead of a separate junction table:**
- Low cardinality (~10 event types per vendor max)
- Read-heavy, never partial-update
- Keeps the dispatcher's lookup query simple (single row)
- Easy to read in admin tools and SQL queries

**Why JSON for ConfigJson instead of typed columns:**
- Each vendor has different config needs (FK: apiKey + billToCode; P44: oauthClientId + oauthClientSecret)
- Adding a vendor doesn't require ALTER TABLE
- Each adapter parses its own ConfigJson — type safety lives in the adapter, not the schema
- Tradeoff: no SQL-level validation; bad config surfaces at adapter init time, which is fine

### 3.2 `VendorOutboundTransactions` — the outbound audit log

**One row per dispatch attempt to a vendor.** The single most important table — answers "did we send it?", "what did they say?", "how often are we failing?", and feeds the 95% SLA dashboard.

**Status lifecycle:**

```
PENDING ──┬──► ACK ──┬──► CONFIRMED  (vendor's webhook confirmed it landed)
          │          └──► REJECTED   (vendor's webhook reported errors)
          │
          ├──► HTTP_FAIL              (4xx synchronous; vendor rejected the payload)
          ├──► TRANSPORT_FAIL         (network failed before HTTP response)
          ├──► RATE_LIMITED           (429; will retry)
          ├──► SKIPPED                (no profile matched; logged for visibility)
          └──► DEAD_LETTER            (exhausted retries)
```

**Heavy index strategy:** because the dashboard runs aggregate queries by vendor + status + time window, those columns are indexed. Also indexed by VectorLoadId so the "everything about load X" report is fast.

**Why `RequestPayload` and `ResponseBody` are NVARCHAR(MAX) and not compressed:**
- SQL Server compresses MAX columns automatically when stored off-row
- Searching response bodies is rare; when needed it's tolerable
- Compression libraries would add code complexity for minimal gain at this scale
- A retention policy (purge transactions older than N days) is cheaper than compression

### 3.3 `VendorInboundCallbacks` — the inbound audit log

**One row per webhook we receive.** Dedupe by `(VendorName, PayloadHash)` UNIQUE constraint.

**Why the unprocessed-rows filtered index:**
The `WebhookCorrelator` runs every 10 seconds with: `SELECT ... WHERE ProcessedUtc IS NULL`. At low rates of inbound (a few hundred per day) the table is small enough that a full scan is fine — but as volume scales (Phase 2 FBS adds more dispatch volume → more confirmations), a filtered index on `ProcessedUtc IS NULL` keeps the correlator fast forever.

### 3.4 `LoadCrossReference` — the framework-pure alternative

This table is the **architectural question** mentioned in the preamble.

**Two options for storing vendor-specific load IDs:**

**Option A (existing-script style):** Add columns to Vector's `Load` table:
```sql
ALTER TABLE dbo.[Load] ADD
    FourKitesLoadId BIGINT NULL,
    FourKitesCreatedUtc DATETIME2 NULL,
    FourKitesTrackingStatus NVARCHAR(20) NULL;
```

Pros: Simple. Single-row lookup from Vector code.
Cons: Vendor #2 requires another `ALTER TABLE Load`. Vendor #3 too. The Vector core table accumulates vendor-specific cruft.

**Option B (framework-pure):** Cross-reference table in `VendorAPI_FK`:
```sql
CREATE TABLE dbo.LoadCrossReference (
    VectorLoadId NVARCHAR(50) NOT NULL,
    VendorName   NVARCHAR(50) NOT NULL,
    VendorLoadId NVARCHAR(100) NOT NULL,
    TrackingStatus NVARCHAR(20) NULL,
    CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_LoadCrossReference PRIMARY KEY (VectorLoadId, VendorName)
);
```

Pros: Vendor #2 needs zero schema change. Pure framework. One row per (load, vendor) pair.
Cons: Cross-database join when Vector code wants to know "what's the FK ID for this load?" — slightly slower lookup (still fast with proper indexing).

**Recommendation: Option B.** It matches the framework-first direction (D-020/D-025) and avoids polluting Vector's core schema with vendor-specific columns. The slight join overhead is irrelevant at any realistic scale.

**This deliverable includes Option B's table.** If Glen wants Option A instead (for migration ease or any other reason), it's straightforward to add ALTER scripts on Vector side and skip the cross-reference table.

The `FourKitesWebhookProcessor.OnConfirmedAsync` method from Deliverable #5 calls a stored procedure (`sp_RecordVendorLoadCrossReference`) rather than inlining the SQL — that procedure does whichever approach is chosen, and the adapter doesn't care.

### 3.5 `VendorRateLimitWindow` — optional, debatable

The framework's `IRateLimitTracker` interface lets each vendor implement rate limiting however they want. FK's 60/min could be:

**Option α (in-memory only):** Counter in process; resets on app pool recycle. Simpler.
**Option β (DB-backed):** Counters persist across recycles. More accurate but adds DB load.

For Phase 1 with one app instance (OTR API on one IIS server), **Option α is sufficient.** App pool recycles ≈ once daily; losing a window's count is rounding error. Two app instances would call for Option β.

**This deliverable includes the table** but marks it deprecated/optional. If we ever go multi-instance, the table is ready. Until then the FourKites adapter uses in-memory counters.

---

## 4. The "show me everything about load X" query

This is the query that proves D-024 — and the SLA dashboard depends on its components. It's wrapped as a stored procedure for performance and convenience:

```sql
EXEC dbo.usp_GetLoadAuditTrail @VectorLoadId = 'LOAD12345';
```

Returns three result sets:
1. **Outbound transactions** — every call we made for this load to any vendor, in chronological order
2. **Inbound callbacks** — every webhook we received for this load
3. **Cross-references** — vendor IDs assigned to this load

Used by:
- Operations support staff investigating "did FK get our update?"
- The dashboard (Deliverable #8) per-load drill-down
- The handoff package's "how to investigate an issue" runbook

---

## 5. Indexes — sizing for Phase 1

Index choices reflect expected query patterns at Phase 1 volume:

**Daily volume estimates (Phase 1, single shipper, FK only):**
- Outbound transactions: 50 loads × 60 location updates/day = ~3,000 rows/day → ~1M/year
- Inbound callbacks: ~500/day (one per dispatch that produces a webhook)
- ClientProfiles: ~1 row total

**At those volumes, the indexes specified are conservative.** They're sized for the next 5 years without revisitation. If volume scales 100× (multi-shipper Phase 3), the same indexes still work; you might add a couple more for specific dashboard queries that emerge.

**Index naming convention:** `IX_<TableName>_<purpose>` — purpose is what query it serves, not the columns. `IX_VOT_VectorLoad_Recent` not `IX_VOT_VectorLoadId_CreatedUtc`.

---

## 6. Stored procedures

This deliverable creates four procedures. The rest of the system uses ad-hoc parameterized SQL.

| Procedure | Purpose | Called from |
|---|---|---|
| `usp_GetLoadAuditTrail` | Returns full audit story for one VectorLoadId | Support tools, dashboard drill-down |
| `usp_GetSuccessRate` | Aggregate success% over a time window, by vendor and/or event type | Dashboard |
| `usp_UpsertInboundCallback` | Dedupe-aware INSERT (handles UNIQUE constraint conflict cleanly) | `VendorWebhookController` |
| `usp_RecordVendorLoadCrossReference` | Writes (VectorLoadId, VendorName, VendorLoadId) — the abstraction that lets us swap Option A ↔ Option B later | `FourKitesWebhookProcessor.OnConfirmedAsync` |

**Why procs for these specific queries:**
- `usp_GetLoadAuditTrail` — three result sets, simpler from app code as one call
- `usp_GetSuccessRate` — complex window aggregations; better in SQL
- `usp_UpsertInboundCallback` — handles the upsert race condition atomically (MERGE with retry)
- `usp_RecordVendorLoadCrossReference` — abstracts Option A vs Option B; app code doesn't know which

Everything else stays as parameterized ad-hoc SQL from the repository classes. We're not building a stored-procedure-everywhere shop.

---

## 7. Running the scripts

### 7.1 First-time deployment

Run scripts in numeric order. Each script targets `VendorAPI_FK` and is idempotent.

```cmd
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -i scripts\00_CreateDatabase.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\01_ClientProfiles.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\02_VendorOutboundTransactions.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\03_VendorInboundCallbacks.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\04_LoadCrossReference.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\05_VendorRateLimitWindow.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\06_StoredProcedures.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\07_SeedData.sql
sqlcmd -S 10.10.9.10\SQLEXPRESS12 -E -d VendorAPI_FK -i scripts\99_Verify.sql
```

`-E` uses integrated Windows auth from the current user. Switch to `-U <user> -P <pass>` if SQL auth is required.

### 7.2 Re-running on an existing database

Every script is idempotent (uses `IF NOT EXISTS` patterns). You can re-run them after changes; they'll add what's new and skip what's already correct.

**Exception:** column type changes are not handled idempotently in these scripts (would require ALTER TABLE with potential data conversion). If you need to change a column type, write a one-off migration script.

### 7.3 Seed data updates

The seed script uses MERGE — re-running it updates the FK ClientProfile row to match the script. **If you've edited the row in the DB (e.g., updated the apiKey), don't re-run the seed script blindly** — it'll overwrite your changes. Or, edit the script first to match the current DB state.

---

## 8. Connection string

For the framework (`Vendor.Common.OutboundTransactionRepository`, etc.) to talk to this DB:

```
Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True;TrustServerCertificate=True
```

This is the value of `VendorDispatch.AuditConnectionString` in OTR API's Web.config (and in any other caller's config).

**Auth choice:** Integrated Security uses the IIS app pool identity. Make sure that identity has appropriate rights:

```sql
USE VendorAPI_FK;
CREATE USER [IIS APPPOOL\OTR_API] FROM LOGIN ...;  -- adjust for your actual app pool
GRANT SELECT, INSERT, UPDATE ON dbo.VendorOutboundTransactions TO [IIS APPPOOL\OTR_API];
GRANT SELECT, INSERT, UPDATE ON dbo.VendorInboundCallbacks TO [IIS APPPOOL\OTR_API];
GRANT SELECT ON dbo.ClientProfiles TO [IIS APPPOOL\OTR_API];
GRANT SELECT, INSERT, UPDATE ON dbo.LoadCrossReference TO [IIS APPPOOL\OTR_API];
GRANT EXECUTE ON dbo.usp_GetLoadAuditTrail TO [IIS APPPOOL\OTR_API];
GRANT EXECUTE ON dbo.usp_UpsertInboundCallback TO [IIS APPPOOL\OTR_API];
GRANT EXECUTE ON dbo.usp_RecordVendorLoadCrossReference TO [IIS APPPOOL\OTR_API];
-- Dashboard reporting account needs SELECT and EXECUTE on success-rate proc, nothing else
GRANT SELECT ON SCHEMA::dbo TO [Reporting_RO];
GRANT EXECUTE ON dbo.usp_GetSuccessRate TO [Reporting_RO];
GRANT EXECUTE ON dbo.usp_GetLoadAuditTrail TO [Reporting_RO];
```

The "Reporting_RO" account is a placeholder — Glen has Vector's own reporting user setup pattern.

---

## 9. Open items

| ID | Item | Resolution needed before |
|---|---|---|
| O-701 | Confirm Option A (Vector Load table additions) vs Option B (LoadCrossReference table) — this deliverable assumes B | Schema deployment |
| O-702 | Retention policy: how long do we keep `VendorOutboundTransactions` rows? Recommend 90 days, archival to a history table after that | Long-term operation |
| O-703 | Production password/credential management for `ClientProfiles.ConfigJson` — encrypt at rest? Always Encrypted column? | Production deployment |
| O-704 | What identity does Vector FBS run as when it directly references `Vendor.FourKites.dll` (Phase 2)? Needs its own GRANTs | Phase 2 |
| O-705 | Backup/restore policy for `VendorAPI_FK` | Operational handoff |

---

## 10. Done-when checklist

Mark this deliverable complete when:

- [ ] `VendorAPI_FK` database exists on `10.10.9.10\SQLEXPRESS12`
- [ ] All scripts in `scripts/` folder run without errors against a fresh database
- [ ] All scripts re-run without errors against the same database (idempotency check)
- [ ] `99_Verify.sql` reports all green
- [ ] One FK ClientProfile row exists with valid (sandbox) `apiKey` and `billToCode`
- [ ] OTR API's app pool identity has appropriate GRANTs
- [ ] `usp_GetLoadAuditTrail` returns expected results when called with a test load ID after running a manual outbound transaction insert
- [ ] Connection string from OTR API works (Vendor.FourKites SmokeTest succeeds end-to-end)

---

## 11. File index

The actual DDL scripts:

| File | Purpose |
|---|---|
| `scripts\00_CreateDatabase.sql` | Database creation |
| `scripts\01_ClientProfiles.sql` | Routing config |
| `scripts\02_VendorOutboundTransactions.sql` | Outbound audit |
| `scripts\03_VendorInboundCallbacks.sql` | Inbound audit |
| `scripts\04_LoadCrossReference.sql` | Vendor ID mappings (Option B) |
| `scripts\05_VendorRateLimitWindow.sql` | Optional DB-backed rate limits |
| `scripts\06_StoredProcedures.sql` | The four procs |
| `scripts\07_SeedData.sql` | FK ClientProfile sample |
| `scripts\99_Verify.sql` | Post-deployment verification |

---

*End of SQL Schema deliverable. The actual DDL scripts follow in the `scripts/` folder.*
