# Refactor Plan — FourKitesIntegration → Vendor.* Framework

**Document:** Deliverable #2 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** Master Strategy doc (Deliverable #1) reviewed and accepted
**Related decisions:** D-018, D-019, D-020, D-024

---

## 0. Purpose

Transform the existing `FourKitesIntegration` solution at
`C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\FourKitesIntegration\fourkites-code\`
into the framework-aware structure:

- **`Vendor.Common`** — new shared framework DLL
- **`Vendor.FourKites`** — refactored from `FourKitesIntegration.Core`
- **`Vendor.FourKites.SmokeTest`** — refactored from `FourKitesIntegration.SmokeTest`
- **`SqlMigrations`** — renamed scripts in `VendorAPI_FK` style (Deliverable #7 produces the actual DDL)

Deprecate but retain as reference:
- `FourKitesIntegration.OutboundService`
- `FourKitesIntegration.WebhookReceiver`

The goal of this deliverable is **a working compilable solution** with the new structure, with NO behavioral changes. New abstractions (ClientProfile, dispatcher, multi-vendor routing) are designed in Deliverable #10 and added later. This refactor is purely structural.

---

## 1. Why we are doing this now

From the Strategy doc:

- **D-018** — Future-proof naming costs nothing today; expensive once references multiply
- **D-020** — Framework-first; `Vendor.Common` is foundational, not optional
- **D-024** — One DLL per vendor; the existing `FourKitesIntegration.Core` becomes `Vendor.FourKites` and absorbs the WebhookReceiver's reusable assets

This deliverable does NOT:
- Add new functionality
- Change any payload shapes or endpoints
- Move any code out of `FourKitesIntegration.Core` that doesn't belong elsewhere
- Touch OTR API
- Touch VB.NET app
- Touch FBS

It is a **rename + relocation + re-target** exercise. After this is done, the solution compiles, the SmokeTest passes, and we are ready to start Deliverable #3 (OTR API upgrade) and #4 (insertion points).

---

## 2. Inventory — what exists today

### Existing solution structure

```
FourKitesIntegration\
└── fourkites-code\
    ├── FourKitesIntegration.sln
    ├── README.md
    ├── FourKitesIntegration.Core\           (targets net481)
    │   ├── Client\
    │   │   ├── FourKitesClient.cs
    │   │   ├── FourKitesClientOptions.cs
    │   │   ├── FourKitesErrorClass.cs
    │   │   ├── FourKitesJson.cs
    │   │   ├── FourKitesResponse.cs
    │   │   └── RateLimitTracker.cs
    │   ├── Mapping\
    │   │   └── Edi214Mapper.cs
    │   ├── Models\
    │   │   ├── Common\
    │   │   │   ├── FourKitesTime.cs
    │   │   │   └── IdentifierKey.cs
    │   │   ├── CreateShipment\
    │   │   │   └── CreateShipmentRequest.cs
    │   │   ├── DispatcherUpdate\
    │   │   │   ├── DispatcherBatch.cs
    │   │   │   └── UpdateTypes.cs
    │   │   ├── Documents\
    │   │   │   └── UploadDocumentRequest.cs
    │   │   └── Webhooks\
    │   │       └── WebhookPayloads.cs
    │   ├── Persistence\
    │   │   └── OutboundTransactionRepository.cs
    │   └── FourKitesIntegration.Core.csproj
    │
    ├── FourKitesIntegration.OutboundService\ (Windows service)
    │   ├── Controllers\DispatchController.cs
    │   ├── Controllers\HealthController.cs
    │   ├── InternalAuthMiddleware.cs
    │   ├── Program.cs
    │   └── App.config
    │
    ├── FourKitesIntegration.WebhookReceiver\ (Windows service)
    │   ├── Controllers\...
    │   ├── Handlers\... (if present)
    │   ├── InboundCallbacksRepository.cs
    │   ├── WebhookAuthMiddleware.cs
    │   ├── WebhookCorrelator.cs
    │   ├── Program.cs
    │   └── App.config
    │
    ├── FourKitesIntegration.SmokeTest\       (console app)
    │   └── (test code)
    │
    └── SqlMigrations\
        ├── 01_OutboundQueue.sql
        ├── 02_OutboundTransactions.sql
        ├── 03_InboundCallbacks.sql
        └── 04_VectorSchemaAdditions.sql
```

### What's in each piece (the "where does this go" assessment)

| Existing file | New home | Why |
|---|---|---|
| `Client\FourKitesClient.cs` | `Vendor.FourKites\Client\` | Vendor-specific outbound client |
| `Client\FourKitesClientOptions.cs` | `Vendor.FourKites\Client\` | FK-specific config |
| `Client\FourKitesErrorClass.cs` | `Vendor.FourKites\Client\` | FK error classifier; **shape** is generalizable but stays vendor-specific in this refactor; abstraction comes in Deliverable #10 |
| `Client\FourKitesJson.cs` | `Vendor.FourKites\Client\` | FK serializer settings (numbers-as-strings etc.) |
| `Client\FourKitesResponse.cs` | `Vendor.FourKites\Client\` | FK response envelope |
| `Client\RateLimitTracker.cs` | `Vendor.FourKites\Client\` | **Stays FK-specific** for now; FK's 60/min is unique to FK. `Vendor.Common` will define `IRateLimitTracker` interface in Deliverable #10 |
| `Mapping\Edi214Mapper.cs` | `Vendor.FourKites\Mapping\` | FK uses EDI 214 codes; vendor-specific |
| `Models\Common\FourKitesTime.cs` | `Vendor.FourKites\Models\Common\` | FK's ISO 8601 format helper |
| `Models\Common\IdentifierKey.cs` | `Vendor.FourKites\Models\Common\` | FK identifier shape (`loadNumber`, `bolNumber`, etc.) |
| `Models\CreateShipment\*` | `Vendor.FourKites\Models\CreateShipment\` | FK create shipment payload |
| `Models\DispatcherUpdate\*` | `Vendor.FourKites\Models\DispatcherUpdate\` | FK dispatcher payload; this is the workhorse |
| `Models\Documents\*` | `Vendor.FourKites\Models\Documents\` | FK POD/document upload |
| `Models\Webhooks\*` | `Vendor.FourKites\Models\Webhooks\` | FK webhook DTOs |
| `Persistence\OutboundTransactionRepository.cs` | **`Vendor.Common\Persistence\`** | **Moves up to framework.** The repository shape is identical for every vendor; only the `VendorName` field varies. See Section 5. |
| `WebhookReceiver\InboundCallbacksRepository.cs` | **`Vendor.Common\Persistence\`** | **Moves up to framework.** Same reasoning. |
| `WebhookReceiver\WebhookCorrelator.cs` | **`Vendor.Common\Persistence\`** | **Moves up to framework.** Correlation by `requestId` is vendor-agnostic. |
| `WebhookReceiver\WebhookAuthMiddleware.cs` | **`Vendor.FourKites\Webhooks\`** (validator extracted as a class) | FK-specific auth scheme. The middleware pattern goes away — OTR API hosts the endpoint now, not a separate service. The reusable piece is the **signature validator class** extracted from this middleware. |
| `WebhookReceiver\Controllers\*` | **Reference only** — superseded by OTR API's `FourKitesWebhookController` (Deliverable #5) | The HTTP endpoint moves to OTR API per D-014 |
| `WebhookReceiver\Program.cs` | **Reference only** — superseded | OTR API's IIS hosting replaces the standalone service |
| `OutboundService\*` | **Reference only** — kept for HTTP-envelope pattern, not deployed | Per D-019, callers reference the DLL directly |
| `SmokeTest\*` | `Vendor.FourKites.SmokeTest\` | Renamed; tests the FK client end-to-end |
| `SqlMigrations\*` | Replaced by Deliverable #7 (`VendorAPI_FK` schema) | Old scripts targeted `VectorOTR` add-ons; new scripts target the new `VendorAPI_FK` database |

---

## 3. Target solution structure

```
FourKitesIntegration\           ← folder name stays for now (TFS path stability)
└── fourkites-code\             ← folder name stays for now
    ├── VendorIntegration.sln   ← renamed
    ├── README.md               ← rewritten to describe Vendor.* framework
    │
    ├── Vendor.Common\          ← NEW project
    │   ├── Vendor.Common.csproj
    │   ├── Abstractions\
    │   │   ├── IVendorClient.cs              (marker interface, minimal)
    │   │   ├── IRateLimitTracker.cs          (interface only; impls stay vendor-specific)
    │   │   └── IWebhookSignatureValidator.cs (interface only)
    │   ├── Persistence\
    │   │   ├── OutboundTransaction.cs        ← moved from FourKitesIntegration.Core
    │   │   ├── OutboundTransactionRepository.cs ← moved + add VendorName column writes
    │   │   ├── InboundCallback.cs            ← extracted from WebhookReceiver
    │   │   ├── InboundCallbackRepository.cs  ← moved from WebhookReceiver
    │   │   └── WebhookCorrelator.cs          ← moved from WebhookReceiver
    │   ├── Errors\
    │   │   └── ErrorClassification.cs        (enum: Transient | Permanent | RateLimit | Unknown)
    │   └── (NO ClientProfile yet — that's Deliverable #10)
    │
    ├── Vendor.FourKites\       ← RENAMED from FourKitesIntegration.Core
    │   ├── Vendor.FourKites.csproj
    │   ├── Client\
    │   │   ├── FourKitesClient.cs            ← namespace updated
    │   │   ├── FourKitesClientOptions.cs
    │   │   ├── FourKitesErrorClass.cs
    │   │   ├── FourKitesJson.cs
    │   │   ├── FourKitesResponse.cs
    │   │   └── FourKitesRateLimitTracker.cs  ← renamed, implements IRateLimitTracker
    │   ├── Mapping\
    │   │   └── Edi214Mapper.cs
    │   ├── Models\
    │   │   ├── Common\
    │   │   ├── CreateShipment\
    │   │   ├── DispatcherUpdate\
    │   │   ├── Documents\
    │   │   └── Webhooks\
    │   └── Webhooks\                          ← NEW folder in this project
    │       ├── FourKitesWebhookSignatureValidator.cs  ← extracted from WebhookAuthMiddleware
    │       └── FourKitesWebhookPayloadParser.cs       ← extracted from WebhookReceiver
    │
    ├── Vendor.FourKites.SmokeTest\    ← RENAMED
    │   └── (existing tests, namespaces updated)
    │
    ├── _Reference\             ← NEW folder — kept for documentation value, NOT in solution
    │   ├── FourKitesIntegration.OutboundService\  ← MOVED here, removed from .sln
    │   └── FourKitesIntegration.WebhookReceiver\  ← MOVED here, removed from .sln
    │
    └── SqlMigrations\          ← will be replaced by Deliverable #7's VendorAPI_FK schema
        └── (old scripts retained until Deliverable #7 is applied)
```

### Why the `_Reference\` folder

The OutboundService and WebhookReceiver represent real engineering work (the HTTP envelope pattern, the auth middleware, the controller layout). They're not garbage; they're just superseded by D-010 (webhook in OTR API) and D-019 (direct DLL reference). Keeping them in a `_Reference\` folder, outside the solution file but in the repository, means:

- Future you (or a new developer) can see the pattern if they need to spin up a standalone service for a future vendor
- TFS history is preserved
- They don't build, don't deploy, don't confuse anyone about what's live

If you'd rather just delete them, that's fine too — TFS still has the history. I default to keeping them because storage is cheap and memory is expensive.

---

## 4. Namespace migration table

This is the single source of truth for the rename. Every file affected by namespace changes is listed.

| Old namespace | New namespace | Files affected |
|---|---|---|
| `FourKitesIntegration.Core` | `Vendor.FourKites` | All `Client\*.cs` |
| `FourKitesIntegration.Core.Client` | `Vendor.FourKites.Client` | `FourKitesClient.cs`, `FourKitesClientOptions.cs`, `FourKitesErrorClass.cs`, `FourKitesJson.cs`, `FourKitesResponse.cs`, `RateLimitTracker.cs` (renamed to `FourKitesRateLimitTracker.cs`) |
| `FourKitesIntegration.Core.Mapping` | `Vendor.FourKites.Mapping` | `Edi214Mapper.cs` |
| `FourKitesIntegration.Core.Models.Common` | `Vendor.FourKites.Models.Common` | `FourKitesTime.cs`, `IdentifierKey.cs` |
| `FourKitesIntegration.Core.Models.CreateShipment` | `Vendor.FourKites.Models.CreateShipment` | `CreateShipmentRequest.cs` |
| `FourKitesIntegration.Core.Models.DispatcherUpdate` | `Vendor.FourKites.Models.DispatcherUpdate` | `DispatcherBatch.cs`, `UpdateTypes.cs` |
| `FourKitesIntegration.Core.Models.Documents` | `Vendor.FourKites.Models.Documents` | `UploadDocumentRequest.cs` |
| `FourKitesIntegration.Core.Models.Webhooks` | `Vendor.FourKites.Models.Webhooks` | `WebhookPayloads.cs` |
| `FourKitesIntegration.Core.Persistence` | **`Vendor.Common.Persistence`** | `OutboundTransactionRepository.cs`, `OutboundTransaction` POCO |
| `FourKitesIntegration.WebhookReceiver` | **`Vendor.Common.Persistence`** | `InboundCallbacksRepository.cs`, `WebhookCorrelator.cs` |
| `FourKitesIntegration.WebhookReceiver` | **`Vendor.FourKites.Webhooks`** | Extracted from `WebhookAuthMiddleware.cs` → `FourKitesWebhookSignatureValidator.cs` |
| `FourKitesIntegration.SmokeTest` | `Vendor.FourKites.SmokeTest` | All test files |

**Visual Studio tip:** Use **Right-click → Refactor → Rename Namespace** (or `Ctrl+R, Ctrl+R` on the namespace declaration) to propagate changes across the codebase. Do this in the order shown in Section 6 (innermost dependencies first).

---

## 5. Required code changes (beyond rename)

These are the only places where actual code changes — not just namespace updates — are needed in this refactor. Everything else is pure rename/relocate.

### 5.1 OutboundTransaction POCO — add VendorName

```csharp
// File: Vendor.Common\Persistence\OutboundTransaction.cs
public class OutboundTransaction
{
    public long TransactionId { get; set; }
    public string VendorName { get; set; }       // NEW — "FourKites", "Project44", etc.
    public string VectorLoadId { get; set; }
    // ... rest unchanged
}
```

### 5.2 OutboundTransactionRepository — add VendorName to INSERT

```csharp
// Add @VendorName parameter to InsertPendingAsync's command
const string sql = @"
INSERT INTO dbo.VendorOutboundTransactions
    (VendorName, VectorLoadId, UpdateType, ...)
OUTPUT INSERTED.TransactionId
VALUES
    (@VendorName, @VectorLoadId, @UpdateType, ...);";
// ...
cmd.Parameters.AddWithValue("@VendorName", (object)tx.VendorName ?? DBNull.Value);
```

**Note on table name:** the new table is `dbo.VendorOutboundTransactions` in the `VendorAPI_FK` database — see Deliverable #7. The repository's connection string targets the new DB.

### 5.3 InboundCallback POCO — add VendorName

Same pattern. Inbound callback rows need to know which vendor sent them.

### 5.4 FourKitesClient — set VendorName when persisting

```csharp
// When constructing OutboundTransaction before persist:
var tx = new OutboundTransaction
{
    VendorName = "FourKites",              // NEW — hard-coded in the FK client
    VectorLoadId = batch.VectorLoadId,
    UpdateType = batch.UpdateType,
    // ...
};
```

Hard-coding `"FourKites"` is fine — each vendor DLL knows its own name. Don't introduce a constant in `Vendor.Common` for this; that's premature.

### 5.5 IRateLimitTracker interface (Vendor.Common)

Minimal interface so `Vendor.Common`'s logging code can ask "did we get rate-limited?" without caring about the per-vendor limit specifics.

```csharp
// File: Vendor.Common\Abstractions\IRateLimitTracker.cs
public interface IRateLimitTracker
{
    Task<bool> TryAcquireAsync(CancellationToken ct = default);
    void RecordRateLimitResponse(TimeSpan retryAfter);
    RateLimitState GetCurrentState();
}

public class RateLimitState
{
    public int RemainingThisWindow { get; set; }
    public DateTime WindowResetUtc { get; set; }
    public bool IsCurrentlyLimited { get; set; }
}
```

`RateLimitTracker` in `Vendor.FourKites` (renamed to `FourKitesRateLimitTracker`) implements this interface. Existing internal logic doesn't change.

### 5.6 IWebhookSignatureValidator interface (Vendor.Common)

Same pattern. Lets `Vendor.Common` write generic "validate this webhook" code that delegates to the vendor-specific implementation.

```csharp
// File: Vendor.Common\Abstractions\IWebhookSignatureValidator.cs
public interface IWebhookSignatureValidator
{
    /// <summary>
    /// Validates a webhook request. Returns true if authentic.
    /// </summary>
    bool IsValid(IDictionary<string, string> headers, string rawBody);
}
```

`FourKitesWebhookSignatureValidator` (extracted from the old `WebhookAuthMiddleware`) implements this.

### 5.7 IVendorClient marker interface (Vendor.Common)

Deliberately tiny. Don't put methods in this interface yet — every vendor's API surface is different. The marker exists so future polymorphic code (the dispatcher) can hold an `IVendorClient` reference.

```csharp
// File: Vendor.Common\Abstractions\IVendorClient.cs
public interface IVendorClient
{
    string VendorName { get; }
}
```

`FourKitesClient` adds `public string VendorName => "FourKites";` — one line.

---

## 6. Execution order (step by step)

Each step ends with a **verify** action. Don't proceed if verify fails.

### Step 1 — Back up

Make a copy of the entire `FourKitesIntegration\fourkites-code\` folder. Tag the SVN revision. We're doing a structural change; rollback should be one move.

**Verify:** backup folder exists; SVN history is intact.

### Step 2 — Move OutboundService and WebhookReceiver to `_Reference\`

```
fourkites-code\
├── _Reference\
│   ├── FourKitesIntegration.OutboundService\
│   └── FourKitesIntegration.WebhookReceiver\
```

Remove these projects from the solution file (right-click → Remove in Visual Studio). Don't delete the files.

Before removing WebhookReceiver from the solution, **copy out**:
- `InboundCallbacksRepository.cs`
- `WebhookCorrelator.cs`
- `WebhookAuthMiddleware.cs` (we'll extract the validator class from this in Step 6)

Put copies temporarily in `fourkites-code\_pending\` so they're not lost.

**Verify:** Solution still loads. The two services are gone from Solution Explorer. The copies in `_pending\` exist.

### Step 3 — Create `Vendor.Common` project

In Visual Studio: Add → New Project → Class Library (.NET Framework), target **.NET Framework 4.8.1**, named `Vendor.Common`. Location: `fourkites-code\Vendor.Common\`.

In the new project file:
```xml
<RootNamespace>Vendor.Common</RootNamespace>
<AssemblyName>Vendor.Common</AssemblyName>
```

Create folders inside the project:
- `Abstractions\`
- `Persistence\`
- `Errors\`

Add NuGet references: none required initially. (`OutboundTransactionRepository` uses `System.Data.SqlClient` from the GAC.)

**Verify:** Vendor.Common builds (empty but valid).

### Step 4 — Move repositories and correlator into Vendor.Common

Move from temporary `_pending\` and from `FourKitesIntegration.Core\Persistence\`:

- `OutboundTransactionRepository.cs` → `Vendor.Common\Persistence\` (namespace becomes `Vendor.Common.Persistence`)
- `InboundCallbacksRepository.cs` → `Vendor.Common\Persistence\`
- `WebhookCorrelator.cs` → `Vendor.Common\Persistence\`

Apply changes from Section 5.1, 5.2, 5.3 (add `VendorName` column writes).

**Verify:** Vendor.Common builds. Repository SQL still references the OLD table names (`FourKitesOutboundTransactions`, etc.) — that's fine for now; Deliverable #7 introduces the new table names. We're rename-only at this step.

### Step 5 — Create abstractions in Vendor.Common

Create the three interfaces from Section 5.5, 5.6, 5.7:
- `Abstractions\IVendorClient.cs`
- `Abstractions\IRateLimitTracker.cs` (plus `RateLimitState` POCO)
- `Abstractions\IWebhookSignatureValidator.cs`

Plus the error classification enum:
- `Errors\ErrorClassification.cs`

**Verify:** Vendor.Common builds.

### Step 6 — Rename `FourKitesIntegration.Core` → `Vendor.FourKites`

In Visual Studio:
1. Right-click the project in Solution Explorer → Rename → `Vendor.FourKites`
2. Open the .csproj and change `<RootNamespace>` and `<AssemblyName>` to `Vendor.FourKites`
3. Close the solution, rename the folder on disk from `FourKitesIntegration.Core\` to `Vendor.FourKites\`
4. Open the .sln in a text editor and update the project path and project name
5. Reopen the solution

In each source file, update `namespace FourKitesIntegration.Core.*` to `namespace Vendor.FourKites.*`. Visual Studio's namespace rename refactor handles dependent references.

Specific renames within the project:
- `Client\RateLimitTracker.cs` → `Client\FourKitesRateLimitTracker.cs`
- Inside, class declaration: `public class FourKitesRateLimitTracker : IRateLimitTracker`

Add project reference: `Vendor.FourKites` → `Vendor.Common`.

Extract from the copy of `WebhookAuthMiddleware.cs` in `_pending\` the signature-validation logic into a new class:
- `Webhooks\FourKitesWebhookSignatureValidator.cs` implementing `IWebhookSignatureValidator`

The middleware *itself* (the OWIN/Web API plumbing) is discarded; OTR API has its own request pipeline.

**Verify:** Vendor.FourKites builds. SmokeTest may not yet — that's Step 7.

### Step 7 — Rename SmokeTest project

Same procedure as Step 6, applied to `FourKitesIntegration.SmokeTest\` → `Vendor.FourKites.SmokeTest\`.

Update its references:
- Remove reference to `FourKitesIntegration.Core`
- Add reference to `Vendor.FourKites` and `Vendor.Common`

Update `using` statements in test files.

**Verify:** SmokeTest builds. Run it (assuming you have FourKites sandbox credentials configured); confirm it still successfully calls the FK API.

### Step 8 — Rename the solution file

`FourKitesIntegration.sln` → `VendorIntegration.sln`. Update solution name in Visual Studio.

**Verify:** Solution opens, three projects show: Vendor.Common, Vendor.FourKites, Vendor.FourKites.SmokeTest. Build → Rebuild All succeeds.

### Step 9 — Update README

Replace existing README in `fourkites-code\` with a new one that:
- Describes the Vendor.* framework structure
- Explains the role of each project
- Notes that `_Reference\` is non-deploying historical reference
- Links to the Strategy doc and Deliverable #4 (insertion points) for usage

Draft README is provided in Section 8 below.

**Verify:** Anyone opening the solution for the first time can read the README and understand the layout.

### Step 10 — Clean up `_pending\`

If everything in Steps 4–6 worked, the temporary `_pending\` folder is no longer needed. Delete it. The originals in `_Reference\FourKitesIntegration.WebhookReceiver\` are still there for historical reference.

**Verify:** `_pending\` is gone; nothing referenced it.

### Step 11 — Commit to SVN

One commit, one message: `"Refactor: FourKitesIntegration → Vendor.* framework structure (Deliverable #2). No behavioral changes."`

**Verify:** SVN log shows the commit. Folder structure on disk matches Section 3.

---

## 7. What's NOT done in this refactor (deferred)

These appear in the Strategy doc and Section 6 of this doc as future work. They are *not* in this deliverable to keep the refactor scope tight:

| Item | When |
|---|---|
| ClientProfile entity + repository | Deliverable #10 (framework design) — built later in Phase 3 |
| VendorDispatcher class | Deliverable #10 |
| LoadCrossReferenceRepository | Deliverable #7 (SQL) + later code addition |
| SuccessRateCalculator | Deliverable #8 (dashboard) |
| VendorErrorClassifier class (full impl) | Deliverable #10 — only the enum is created here |
| Polly retry policy factory abstraction | Deliverable #10 — current Polly usage stays in Vendor.FourKites for now |
| New SQL tables in `VendorAPI_FK` | Deliverable #7 |

The principle: **this refactor is structural, not behavioral.** Anything that changes what the code *does* belongs in a later deliverable.

---

## 8. README replacement (draft)

```markdown
# Vendor Integration Platform

This solution contains the framework and vendor-specific implementations for Vector's
external API integrations. The first vendor is **FourKites**; future vendors plug into
the same framework.

## Projects

| Project | Purpose |
|---|---|
| `Vendor.Common` | Framework shared by all vendor DLLs. Audit log repository, webhook correlation, vendor interfaces. References nothing outside the .NET BCL and Newtonsoft.Json. |
| `Vendor.FourKites` | FourKites-specific client, DTOs, mapping, and webhook validator. References `Vendor.Common`. This is the DLL referenced by OTR API, Vector FBS (Phase 2), and the VB.NET POD application. |
| `Vendor.FourKites.SmokeTest` | Console app that exercises the FourKites client end-to-end against the FK sandbox. Used for verifying credentials, payload shapes, and webhook signature validation. |

## What's in `_Reference\`

The folder `_Reference\` contains earlier project structures that are no longer built or
deployed:

- `FourKitesIntegration.OutboundService\` — original design had Vector FBS POST to this
  Windows Service over HTTP. Superseded by direct DLL reference (see Decision Log D-019
  in the Strategy doc).
- `FourKitesIntegration.WebhookReceiver\` — original design hosted webhook receivers in a
  standalone Windows Service. Superseded by hosting the webhook endpoint in OTR API
  (see D-010, D-014).

These are kept for historical context and may be useful as reference patterns when
adding a new vendor that warrants a standalone service.

## Related documentation

All design documentation lives in `..\..\_deliverables\`:

- `01_Strategy\01_Master_Strategy.md` — read this first
- `02_Refactor\02_Refactor_Plan.md` — how this solution got from the old structure to this one
- `04_OTR_Insertion_Points\` — how OTR API uses Vendor.FourKites
- `07_SQL_Schema\` — the VendorAPI_FK database
- `10_Framework_Design\` — the multi-vendor abstractions
```

---

## 9. Verification — done when

Mark this deliverable complete when ALL of the following are true:

- [ ] Solution renamed to `VendorIntegration.sln` and opens cleanly
- [ ] Three projects visible: `Vendor.Common`, `Vendor.FourKites`, `Vendor.FourKites.SmokeTest`
- [ ] Rebuild All succeeds with zero errors and zero warnings (warnings about XML doc comments are acceptable)
- [ ] `Vendor.FourKites` references `Vendor.Common`; `Vendor.FourKites.SmokeTest` references both
- [ ] All namespaces match Section 4
- [ ] `OutboundTransaction` and `InboundCallback` POCOs both have `VendorName` property
- [ ] `FourKitesClient` sets `VendorName = "FourKites"` when persisting
- [ ] `IVendorClient`, `IRateLimitTracker`, `IWebhookSignatureValidator` interfaces exist in Vendor.Common
- [ ] `FourKitesRateLimitTracker` implements `IRateLimitTracker`
- [ ] `FourKitesWebhookSignatureValidator` exists and implements `IWebhookSignatureValidator`
- [ ] `_Reference\` folder contains the two old service projects, NOT in the solution
- [ ] README is replaced
- [ ] SmokeTest runs and successfully calls FourKites sandbox
- [ ] SVN commit recorded

---

## 10. Rollback plan

If something goes wrong:

1. Close Visual Studio
2. Delete the `fourkites-code\` folder
3. Restore from the Step 1 backup OR `svn revert -R` to the pre-refactor revision
4. Reopen and rebuild

Estimated rollback time: 5 minutes. The refactor itself is the bigger commitment.

---

## 11. Estimated effort

Assuming Glen is familiar with Visual Studio's rename refactoring tools and the existing codebase:

- Steps 1–2 (backup + move services): 15 min
- Steps 3–5 (create Vendor.Common): 30 min
- Step 6 (rename + extract): 45 min
- Steps 7–9 (smoke test, solution rename, README): 30 min
- Steps 10–11 (cleanup, commit): 10 min
- Buffer for surprises: 30 min

**Total: ~2.5 hours of focused work.**

If anything compiles but tests fail unexpectedly, that's usually a `using` statement missed in a less-obvious file. The rename refactor catches most of these.

---

## 12. Open items specific to this deliverable

None blocking. The refactor can proceed using the existing FourKites endpoint, table names, and connection strings; Deliverables #3 and #7 update those later.

If the SmokeTest currently writes to a real database, you may want to temporarily point it at a scratch database for the rebuild test, then point it back.

---

*End of Refactor Plan.*
