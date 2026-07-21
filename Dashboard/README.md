# VVI Admin Dashboard

Blazor Server (.NET 8) admin app for the Vector Vendor Integration platform.
Reads/writes the **VendorAPI_FK** database. Runs alongside the OTR API.

## Batch 1 — what's in this scaffold

Solution `Vector.VVI.Admin.sln` with 9 projects:

| Project | State |
| --- | --- |
| `Vector.VVI.Admin.Web` | Blazor Server host — Program.cs, MudBlazor, Serilog, cookie auth, layout, nav, Home + Login + Verify page shells. Runs. |
| `Vector.VVI.Admin.Data` | EF Core `VviDbContext` + entities mapped to the **live** schema. Complete. |
| `Vector.VVI.Admin.Auth` | Password hashing (BCrypt), 2FA service, user service, dev-stub email sender, role policies. Service layer complete; UI wiring next. |
| `Vector.VVI.Admin.Common` | Shared time-display helper (UTC→Central with per-field convention). |
| `LiveOps / Transactions / Customers / Profiles / Vendors` | Empty Razor Class Libraries, wired into the solution and referenced by Web. Pages land per feature. |

## Prerequisites

- .NET 8 SDK
- SQL Server access to `VendorAPI_FK` (dev: `(localdb)\mssqllocaldb`)

## First build / run

```
cd Dashboard
dotnet restore
dotnet build
```

Before running, create the two admin tables:

```
sqlcmd -S (localdb)\mssqllocaldb -d VendorAPI_FK -i db\001_AdminTables.sql
```

Then:

```
dotnet run --project Vector.VVI.Admin.Web
```

Browse to the URL shown (default `https://localhost:7280`). You should see the
**Live Ops** shell with four placeholder stat tiles, the nav drawer, and the
`/login` + `/verify-2fa` page shells.

Seeded login (once the login flow is wired next batch): `glen@fullnet247.com`
/ `ChangeMe!2026` — change on first use.

## What to smoke-test for the baseline

1. `dotnet build` succeeds across all 9 projects.
2. App starts, Live Ops page renders with the MudBlazor drawer + app bar.
3. Serilog writes to `C:\InMotion\OTR_Admin\logs\`.
4. `/login` and `/verify-2fa` render their shells.

The DB connection is only exercised once feature pages query it, so the app
starts even before `001_AdminTables.sql` is applied — but apply it before the
Auth batch.

## Decision needed (blocks the Customers feature, not the baseline)

The plan's "Customer Registry via Phase C column adds to `ClientProfiles`"
doesn't match the live schema. Customer identity already lives on
`VVIProfiles` (`CustomerID` + `Customer`); `ClientProfiles` is a separate
shipper-code routing table that already has `IsActive`. Options:

- **A — Derive customers from VVIProfiles** (no schema change): the registry
  lists distinct `CustomerID` + `Customer` from VVIProfiles. Fastest, zero risk.
- **B — New `Customers` master table** (`CustomerID` PK, Name, TechContact,
  IsActive) and FK `VVIProfiles.CustomerID` to it. Cleanest long-term; one
  new table + a backfill.
- **C — Original plan** (add customer columns to `ClientProfiles`) — not
  recommended; it mixes customer master data into the routing table.

Recommendation: **B** if you want a real contact/registry record per customer,
otherwise **A**. No `ALTER` runs until you pick.
