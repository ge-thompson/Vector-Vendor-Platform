# OTR API .NET Framework 4.8.1 Upgrade Playbook

**Document:** Deliverable #3 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Risk level:** HIGH — OTR API is production with multiple consumers
**Estimated effort:** 3–4 hours under good conditions; 1 day if surprises
**Prerequisites:** Master Strategy doc reviewed; Deliverable #2 refactor plan understood (but Deliverable #2 itself does NOT need to be executed before this one)
**Related decisions:** D-013, D-014

---

## 0. Purpose and framing

Upgrade OTR API from .NET Framework 4.6.1 to 4.8.1. This is a prerequisite for OTR API referencing `Vendor.FourKites.dll`, which targets 4.8.1.

**This playbook treats the upgrade as a production change with real risk.** OTR API serves:
- Vector FBS (load submission, status retrieval)
- TruckTools (status webhooks inbound)
- Historical mobile app users (`inmotionreset.aspx`, `Portal.Master`)

Breakage in any consumer = a customer-visible outage. The playbook structure reflects that risk:

1. Understand what changes
2. Inventory consumers and dependencies
3. Capture baseline behavior (so you know what "still works" looks like)
4. Execute the upgrade step by step with verify gates
5. Run smoke tests against each consumer
6. Have a rollback path that takes 10 minutes

**Cardinal rule:** if anything in Section 5 verification fails, **roll back** (Section 6). Don't try to fix forward during the upgrade window. Diagnose the failure separately, plan a fix, and retry the upgrade as a fresh attempt.

---

## 1. What actually changes between 4.6.1 and 4.8.1

The list of "what 4.8.1 added" is long and mostly irrelevant. Here's what matters for OTR API specifically.

### 1.1 In-place runtime upgrade

.NET Framework is an in-place upgrade on the server. Once 4.8.1 is installed, **every** app targeting 4.6.1, 4.6.2, 4.7.x, or 4.8 runs on the 4.8.1 runtime. You don't get to upgrade one app without affecting others. **Verify that no other apps on the OTR API host depend on 4.6.x-specific quirks before proceeding.**

The retarget (changing OTR API's project file from `v4.6.1` to `v4.8.1`) only affects which framework features the *compiler* allows OTR API to use. It does not affect what runtime OTR API runs on.

### 1.2 Specific behaviors that have changed (4.6.1 → 4.8.1)

| Area | What changed | OTR API risk |
|---|---|---|
| **TLS defaults** | Default protocols now include TLS 1.2 explicitly; legacy SSL3 and TLS 1.0 paths deprecated | Low — OTR API's outbound calls (FBS WCF service) already work; verify after upgrade |
| **HttpClient pooling** | Connection pool behavior tweaked; long-lived HttpClient instances behave slightly differently | Low — OTR API uses System.Net.Http but mostly for inbound |
| **WCF client serialization** | DataContractSerializer tightened on certain edge cases | **MEDIUM** — the `FBSCheckCallSrv` service reference might regenerate differently. Plan to re-add the service reference if WCF calls fail post-upgrade. |
| **WebForms compile** | aspx compilation switched to Roslyn by default in newer Microsoft.CodeDom.Providers versions | **MEDIUM** — old `Microsoft.Net.Compilers 1.0.0` doesn't support C# 7.3+; must upgrade to 3.6.0+ |
| **Settings designer** | Properties\Settings.Designer.cs regeneration on framework change | Low — usually transparent |
| **ASP.NET request validation** | Slightly stricter on malformed input | Low — OTR API handles JSON, not user input |
| **System.Net.Mail** | If used, default SmtpClient behavior tweaked | Not used in OTR API |

### 1.3 What does NOT change

- IIS hosting model (still IIS)
- Web API 2 routes (Microsoft.AspNet.WebApi 5.2.3 supports 4.8.1)
- HMAC auth filter behavior
- Newtonsoft.Json behavior (10.0.1 still works on 4.8.1; upgrading it is optional)
- Database access (System.Data.SqlClient)
- Existing controller endpoints

The bottom line: **the runtime upgrade itself is low-risk. The compile-and-build pipeline is where the surprises live**, specifically Microsoft.Net.Compilers and Microsoft.CodeDom.Providers.

---

## 2. Dependency inventory

### 2.1 NuGet packages and their 4.8.1 compatibility

From `packages.config`:

| Package | Current | 4.8.1 compatible? | Action |
|---|---|---|---|
| GeoTimeZone | 4.1.0 | ✅ Yes (net461 lib works on 4.8.1) | No change |
| Microsoft.AspNet.WebApi | 5.2.3 | ✅ Yes | No change |
| Microsoft.AspNet.WebApi.Client | 5.2.3 | ✅ Yes | No change |
| Microsoft.AspNet.WebApi.Core | 5.2.3 | ✅ Yes | No change |
| Microsoft.AspNet.WebApi.WebHost | 5.2.3 | ✅ Yes | No change |
| Microsoft.CodeDom.Providers.DotNetCompilerPlatform | 1.0.0 | ❌ **NO** | **Upgrade to 4.1.0** (latest as of writing) |
| Microsoft.Net.Compilers | 1.0.0 | ❌ **NO** | **Upgrade to 4.8.0** (matches framework target) OR remove entirely (use system compiler) |
| Microsoft.Web.Infrastructure | 1.0.0.0 | ✅ Yes | No change |
| Newtonsoft.Json | 10.0.1 | ✅ Yes (works on 4.8.1) | Optional: bump to 13.0.3 to match `Vendor.FourKites` |
| Swashbuckle | 5.6.0 | ⚠️ Marginal | Test post-upgrade; consider 5.6.0 → maintained fork if needed |
| Swashbuckle.Core | 5.6.0 | ⚠️ Marginal | Same |
| TimeZoneConverter | 5.0.0 | ✅ Yes | No change |
| TimeZoneNames | 5.0.1 | ✅ Yes | No change |
| WebActivatorEx | 2.0 | ✅ Yes | No change |

### 2.2 Manual (non-NuGet) references

From the .csproj:

| Reference | HintPath | Concern |
|---|---|---|
| `Settings` | `..\packages\Settings.dll` | **Unknown DLL.** Not from NuGet (not in packages.config). Needs investigation before upgrade — is it a Vector internal DLL? What framework is it built against? |

**Action item:** Open `..\packages\Settings.dll` and check its target framework with a tool like dotPeek or ILSpy. If it's net461 or earlier and not maintained, copy it to a safe location before upgrade and re-test after.

### 2.3 Service references

| Reference | Concern |
|---|---|
| `Service References\FBSCheckCallSrv` | WCF SOAP client. Generated `Reference.cs` may need regeneration if the framework upgrade changes serializer behavior. |

**Action item:** Before upgrade, note the FBSCheckCallSrv service URL (in `Web.config` under `system.serviceModel/client/endpoint`). Be prepared to right-click the service reference → Update Service Reference after the upgrade.

### 2.4 Server prerequisites

Before any code changes, the OTR API **host machine** must have .NET Framework 4.8.1 installed:

- **Developer machine** (Glen's workstation): Likely already has 4.8.1 — verify in *Programs and Features*
- **Staging/QA server** (if you have one): Must be installed before deploying upgraded code
- **Production IIS server**: Must be installed before deploying

**Download:** Microsoft .NET Framework 4.8.1 (offline installer recommended). Requires Windows Server 2012 R2 or later. Installer reboots if Windows Updates are pending; plan for it.

---

## 3. Consumer inventory — what calls OTR API

This list determines what must be smoke-tested after the upgrade.

| Consumer | What it does | How to test | Owner |
|---|---|---|---|
| Vector FBS | POSTs matched-trip loads to `/api/truckertools/postload`; pulls status updates from `/api/truckertoolstracking/getstatuses` (or similar) | Submit a test load through FBS, verify it reaches TruckTools | Vector dev team |
| TruckTools webhooks | POSTs status updates to `/api/truckertoolstracking/sendstatus`; HMAC-signed | TruckTools sandbox → wait for next 15-min cycle, OR ask TT to send a test event | TruckTools |
| TruckTools tracking lifecycle | Calls `/api/truckertoolstracking/tracking`, `/api/truckertoolstracking/updatetrackload`, `/api/truckertoolstracking/cancelloadtracking` | Trigger from FBS UI (start/update/cancel tracking) | TruckTools + Vector |
| Driver mobile app (legacy) | Hits `inmotionreset.aspx` for password reset | Smoke check: page loads; submission writes to DB | Manual UI test |
| Portal pages | `Portal.Master`, `index.html`, `privacy.html`, `termsofuse.html` | Load each in browser; check styling and links | Manual UI test |
| Internal admin tools (any?) | Unknown | Glen to enumerate | Glen |

**Pre-upgrade action:** Confirm this list is complete. If there's a consumer not listed, add it now and define a test.

---

## 4. Pre-upgrade verification — capture baseline

Before changing anything, record what "working" looks like. After the upgrade, the smoke tests must produce the same results.

### 4.1 Functional baseline

Run each of these and save the result somewhere referenceable (screenshot, log entry, file):

| # | Action | Expected result | Capture |
|---|---|---|---|
| B1 | `GET /api/truckertools/health` (or any unauthenticated endpoint, if exists) | 200 OK | Status code and response body |
| B2 | Submit a test load from FBS staging to OTR API | Load appears in TruckTools | TT load ID |
| B3 | Trigger a TT status webhook (sandbox or test event) | Row appears in `VectorOTR_TT` audit table | Row contents |
| B4 | FBS pulls status updates | Returns expected payload | Sample response |
| B5 | Open `inmotionreset.aspx` in browser | Page renders, form posts | Screenshot |
| B6 | Load Swagger UI (if hosted on this app — check `/swagger`) | API list renders | Screenshot |
| B7 | Restart IIS app pool, hit `/api/...` immediately | First request succeeds within a few seconds | Time-to-first-response |

### 4.2 Build baseline

- [ ] Solution opens in Visual Studio without errors
- [ ] Rebuild All succeeds — capture warning count
- [ ] Note the bin output: `bin\OTR API.dll`, manifest, etc. — copy entire bin folder somewhere
- [ ] Run the app locally (IIS Express). Note startup time, memory usage in Task Manager

### 4.3 Environmental baseline

- [ ] Confirm .NET 4.8.1 installer is downloaded and ready
- [ ] Confirm SVN working copy is clean (`svn status` shows nothing pending)
- [ ] Confirm SVN current revision number (`svn info`) — needed for rollback
- [ ] Confirm a backup of the IIS app pool config and site bindings exists (export via IIS Manager → app pool → Export Configuration)
- [ ] Confirm a backup of `Web.config` exists separately

---

## 5. Execution — step by step

### Step 1 — Install .NET Framework 4.8.1 on the build machine (if not present)

1. Download .NET 4.8.1 Developer Pack from Microsoft (developer pack includes targeting libraries for Visual Studio)
2. Run installer; reboot if prompted
3. Confirm via PowerShell:
   ```powershell
   Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\' | Get-ItemProperty -Name Release
   ```
   Expect Release ≥ 533320 (4.8.1).

**Verify:** Visual Studio's "Target framework" dropdown for a Class Library project shows ".NET Framework 4.8.1".

### Step 2 — SVN checkpoint

```cmd
cd "C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\OTR API"
svn info > C:\Backups\OTR-API-pre-481-svn-info.txt
svn status
```

If `svn status` shows pending changes, commit or revert them first. We need a clean baseline.

**Verify:** `svn-info.txt` saved; `svn status` empty.

### Step 3 — Full disk backup

Copy the entire `OTR API` folder to a backup location (outside the working folder, outside any SVN-tracked area):

```cmd
xcopy "C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\OTR API" "C:\Backups\OTR-API-pre-481-backup\" /E /I /H
```

Also back up the deployed IIS site bin folder if it's separate.

**Verify:** Backup folder exists; file count matches source.

### Step 4 — Inspect the unknown `Settings.dll`

Locate `..\packages\Settings.dll` (relative to the OTR API csproj — so one level up at `C:\Users\Glen Thompson\Documents\TFSProjects\FourkitesAPI\packages\Settings.dll`).

Open in dotPeek or ILSpy. Check:
- Target framework version
- Public types (so you know what's at risk)
- Whether it has any 4.6.x-only dependencies

If it's net461 or net462, it should run fine on 4.8.1 runtime. If it's pre-net45, may need a rebuild — coordinate with whoever owns that DLL.

**Document:** What this DLL is, who owns it, what it does. Add to the OTR API README if not already documented.

**Verify:** You know what Settings.dll is and that it's safe to keep using.

### Step 5 — Open solution in Visual Studio

Open `OTR API.sln` in Visual Studio 2022 (17.x) or 2019 (16.11+ — confirm version supports 4.8.1).

Let NuGet restore packages. Build once to confirm baseline still compiles.

**Verify:** Rebuild succeeds with same warning count as Section 4.2.

### Step 6 — Retarget the project to 4.8.1

In Visual Studio:

1. Right-click `OTR API` project → Properties → Application tab
2. Change "Target framework" from ".NET Framework 4.6.1" to ".NET Framework 4.8.1"
3. Confirm any dialog that pops up
4. Visual Studio modifies the .csproj — close Properties

You can also do this manually by editing the .csproj file:

```xml
<!-- Change this line -->
<TargetFrameworkVersion>v4.6.1</TargetFrameworkVersion>
<!-- To this -->
<TargetFrameworkVersion>v4.8.1</TargetFrameworkVersion>
```

**Verify:** The .csproj shows `v4.8.1`. The project icon in Solution Explorer does not show an error.

### Step 7 — Update Web.config

In `Web.config`, find the `<system.web>` section and update the `compilation` and `httpRuntime` elements:

```xml
<!-- Old -->
<compilation debug="true" targetFramework="4.6.1" />
<httpRuntime targetFramework="4.6.1" />

<!-- New -->
<compilation debug="true" targetFramework="4.8.1" />
<httpRuntime targetFramework="4.8.1" />
```

If the existing values are 4.6 or 4.6.2 instead of 4.6.1, update accordingly. Make the same change in `Web.Debug.config` and `Web.Release.config` if they override these values.

**Verify:** Web.config compiles (open in editor; no schema warnings).

### Step 8 — Upgrade Microsoft.Net.Compilers and Microsoft.CodeDom.Providers

The biggest practical change. The old 1.0.0 packages are pre-Roslyn-3.x and won't work with 4.8.1's compilation pipeline correctly.

In Package Manager Console:

```powershell
Update-Package Microsoft.Net.Compilers -Version 4.8.0
Update-Package Microsoft.CodeDom.Providers.DotNetCompilerPlatform -Version 4.1.0
```

Or via Manage NuGet Packages UI → Updates tab → select both → Update.

These updates change the .csproj `<Import>` statements at the top of the file. Verify they now point to the new package versions:

```xml
<Import Project="..\packages\Microsoft.CodeDom.Providers.DotNetCompilerPlatform.4.1.0\build\Microsoft.CodeDom.Providers.DotNetCompilerPlatform.props" ... />
<Import Project="..\packages\Microsoft.Net.Compilers.Toolset.4.8.0\build\Microsoft.Net.Compilers.Toolset.props" ... />
```

(The exact import paths depend on the package versions installed. Microsoft renamed the package to `Microsoft.Net.Compilers.Toolset` at some point — accept whatever NuGet suggests.)

**Verify:** Rebuild All succeeds. The warning count may go up slightly (newer Roslyn surfaces more diagnostics) — don't worry about new warnings unless they include errors or breaking-change advisories.

### Step 9 — Build, run, smoke test locally

1. Rebuild All → expect success
2. Start the app in IIS Express (F5)
3. Hit each baseline test from Section 4.1 — they should all behave the same

If anything fails here, **stop and investigate**. Common issues at this step:

- **WCF service reference exceptions** → right-click `Service References\FBSCheckCallSrv` → Update Service Reference. If that fails, re-add the reference.
- **Roslyn compile errors in .aspx pages** → typically a syntax issue with C# 7+ that newer Roslyn enforces. Fix the .aspx code-behind or downgrade Microsoft.Net.Compilers to 3.x.
- **Unknown Settings.dll incompatibility** → see Step 4 investigation; coordinate with the DLL owner.

**Verify:** All baseline tests from Section 4.1 produce identical results.

### Step 10 — Commit to SVN (build-passing checkpoint)

If Step 9 was clean, commit:

```cmd
svn commit -m "OTR API: Upgrade target framework 4.6.1 → 4.8.1, update Microsoft.Net.Compilers & CodeDom.Providers. Behavior unchanged (Deliverable #3)."
```

This is a checkpoint commit — getting to a building, passing-baseline state. **Do this before deploying anywhere.**

**Verify:** SVN log shows the commit; `svn status` clean.

### Step 11 — Deploy to staging (or local IIS, if no staging)

If you have a staging IIS server:

1. Confirm .NET 4.8.1 is installed on the staging server (per Section 2.4)
2. Publish from Visual Studio or copy the bin folder
3. Recycle the staging app pool

If no staging server, deploy to local IIS (not IIS Express) for a more realistic test.

**Verify:** App pool starts. Initial request to any endpoint returns expected response.

### Step 12 — Run full consumer smoke tests against staging

Re-run every test from Section 3 (consumer inventory) against the staging deployment. Each consumer must work end-to-end.

If any consumer fails: **rollback** (Section 6). Diagnose offline.

**Verify:** Every consumer in Section 3 works against staging.

### Step 13 — Production deployment window

Schedule a window (Friday evening is traditional but Saturday morning is better — fewer real users, more daylight to fix problems):

1. Confirm .NET 4.8.1 is installed on production IIS
2. Take down the app pool gracefully:
   ```powershell
   Stop-WebAppPool -Name "OTR API"
   ```
3. Backup the current `bin\` folder on production
4. Deploy new bin from staging or local build
5. Start app pool:
   ```powershell
   Start-WebAppPool -Name "OTR API"
   ```
6. Hit the first endpoint to force JIT warmup
7. Run smoke tests against production (Section 3, full list)

**Verify:** All consumer smoke tests pass against production within 15 minutes of deployment.

### Step 14 — Watch for 24 hours

For the first 24 hours post-upgrade, monitor:
- Error logs (Windows Event Viewer; OTR API's RawAudit / HTTPLogging output)
- Response times (if you have APM)
- TruckTools webhook success rate (any 4xx/5xx responses from our endpoint to their incoming webhooks?)
- FBS error reports

If error rates rise meaningfully, consider rollback.

**Verify:** No spike in errors after 24 hours.

---

## 6. Rollback procedure

If anything in Steps 9, 12, or 13 fails, roll back. Rollback should take 10 minutes or less.

### From local Step 9 failure

1. Close Visual Studio
2. `svn revert -R .` in the OTR API folder
3. `svn update`
4. Delete the `packages` folder
5. Right-click solution → Restore NuGet packages
6. Build — should be back to 4.6.1 baseline

### From staging Step 12 failure

1. Don't promote to production
2. Roll back locally as above
3. Investigate the failure offline
4. Plan a fix; retry the upgrade as a fresh attempt

### From production Step 13 failure

1. Stop the production app pool
2. Restore the pre-upgrade `bin\` folder backup (from Step 13.3)
3. Start the app pool
4. Verify with a quick smoke test
5. SVN-revert the working copy too, so dev environment matches production again

Maximum time to restore production: **10 minutes if the bin backup is local; 30 minutes if it has to be pulled from elsewhere.**

---

## 7. Common surprises (from "things that bite people doing this exact upgrade")

Listed in approximate likelihood order.

| # | Surprise | Symptom | Fix |
|---|---|---|---|
| 1 | Microsoft.Net.Compilers 1.0.0 not compatible with 4.8.1 | Build fails with "CSC compiler not found" or similar | Update to 4.8.0+ (Step 8 covers this) |
| 2 | WCF service reference uses wrong serializer post-upgrade | FBSCheckCallSrv calls return null or throw | Update Service Reference (right-click in Solution Explorer) |
| 3 | Settings.dll target framework conflict | "Could not load assembly" | Confirm Settings.dll exists in bin; rebuild from source if available |
| 4 | Swashbuckle Swagger UI broken | `/swagger` returns 500 | Either upgrade to a maintained Swashbuckle fork, or remove if Swagger isn't actively used |
| 5 | aspx pages don't compile | "The compiler failed with error code 1" on `inmotionreset.aspx` | Roslyn compiler change — usually requires the Microsoft.CodeDom.Providers upgrade to match |
| 6 | App pool fails to start on production | Event Viewer logs "could not load file or assembly" | Almost always missing .NET 4.8.1 on the server — install and retry |
| 7 | HMAC auth filter behaves differently | Some requests get 401 that previously got 200 | Unlikely but possible if filter relies on framework-specific request stream behavior. Inspect `Filter\HMAC.cs`. |
| 8 | Web.config schema validation warning | Visual Studio shows yellow squiggles under config sections | Cosmetic; ignore unless it blocks build |
| 9 | TFS/SVN check-in fails | Binding file conflicts | Commit before upgrade; resolve any conflicts as you'd normally |
| 10 | Newtonsoft.Json version conflict | "Could not load file or assembly Newtonsoft.Json, Version=10.0.0.0" because `Vendor.FourKites` pulls in 13.x | Add binding redirects to Web.config OR upgrade OTR API's Newtonsoft.Json to 13.0.3 |

Number 10 is the one most likely to bite when you later add the `Vendor.FourKites` reference (Deliverable #4). I recommend upgrading Newtonsoft.Json to 13.0.3 **during** this upgrade window so you're not doing two upgrade conversations at once.

```powershell
Update-Package Newtonsoft.Json -Version 13.0.3
```

---

## 8. Done-when checklist

Mark this deliverable complete when:

- [ ] .NET 4.8.1 installed on all relevant machines (dev, staging, prod)
- [ ] `OTR API.csproj` shows `<TargetFrameworkVersion>v4.8.1</TargetFrameworkVersion>`
- [ ] `Web.config` shows `targetFramework="4.8.1"` in both compilation and httpRuntime
- [ ] Microsoft.Net.Compilers upgraded to 4.x
- [ ] Microsoft.CodeDom.Providers.DotNetCompilerPlatform upgraded to 4.x
- [ ] Optionally: Newtonsoft.Json upgraded to 13.0.3
- [ ] Rebuild All succeeds with zero errors
- [ ] All baseline tests from Section 4.1 pass (local)
- [ ] All consumer smoke tests from Section 3 pass (staging)
- [ ] Production deployed
- [ ] 24-hour watch window completed with no error spike
- [ ] SVN committed
- [ ] Backup folders retained for at least 30 days

---

## 9. What this upgrade enables

Once complete:

- OTR API can reference `Vendor.FourKites.dll` (which targets 4.8.1)
- Deliverable #4 (insertion points) can be executed
- Deliverable #5 (FourKites webhook controller) can be added
- OTR API will have a modern compiler, modern TLS defaults, and modern HttpClient
- Future Vector projects can standardize on 4.8.1 as the floor

---

## 10. Open items specific to this deliverable

| ID | Item | Owner |
|---|---|---|
| O-101 | What is `..\packages\Settings.dll`? Who owns it? | Glen (Step 4) |
| O-102 | Is there a staging server, or only local IIS for pre-production testing? | Glen |
| O-103 | What's the production OTR API URL, IIS site name, and app pool name? | Glen |
| O-104 | Who at TruckTools coordinates webhook test events? | Glen |
| O-105 | Is there a manual UI test scenario for `inmotionreset.aspx` that's documented? | Glen |

These don't block writing the playbook but should be answered before executing it.

---

## 11. Sequencing note

This upgrade is **independent of Deliverable #2 (refactor plan)**. You can execute them in either order:

- **Deliverable #3 first** (upgrade OTR API) — then OTR API is ready to consume the new DLLs once they exist
- **Deliverable #2 first** (refactor the Vendor.* solution) — then the DLLs are ready when OTR API is upgraded

Mild preference for **#3 first** because:
- OTR API upgrade is the bigger risk; getting it out of the way de-risks the rest
- The refactor in #2 is mostly mechanical; less likely to throw surprises
- If #3 fails and has to be deferred, #2 can still proceed independently

But this is operational preference, not a hard sequencing requirement.

---

*End of OTR API Upgrade Playbook.*
