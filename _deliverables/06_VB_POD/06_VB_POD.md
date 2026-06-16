# VB.NET POD Upload — Phase 1

**Document:** Deliverable #6 of 11
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (plan author)
**Prerequisites:** Deliverables #2 (refactor), #7 (SQL schema), #10 (framework) complete; Vendor.FourKites.dll exists; ClientProfile row exists in VendorAPI_FK
**Related decisions:** D-015, D-020, D-024, D-025

---

## 0. Purpose

This deliverable shows how the existing VB.NET application uploads a POD (Proof of Delivery) document to FourKites via the vendor framework. After completion:

- The VB.NET app references `Vendor.Common.dll` and `Vendor.FourKites.dll` directly (no OTR API hop)
- POD uploads use the same `VendorDispatcher` pattern as OTR API
- VB.NET code never names "FourKites" — vendor #2 onboarding requires no VB.NET changes
- Every upload attempt is logged in `VendorAPI_FK.VendorOutboundTransactions` alongside OTR API's outbound dispatches — one audit log, one query

**Pattern parity with #4:** Outbound calls flow through the dispatcher regardless of which caller (OTR API webservice, VB.NET desktop, FBS in Phase 2). The framework abstraction holds.

---

## 1. What I don't know yet (and how I'm handling it)

The strategy doc has open items I'm acknowledging up front rather than designing around assumptions:

| Open item | Status | This deliverable's approach |
|---|---|---|
| **O-004:** Which specific VB.NET application is this? | Not confirmed | Sample code targets VB.NET on .NET Framework 4.8.1 with WinForms or class library equivalence. Same code works on 4.7.x. |
| **Where does the VB.NET app get the VectorLoadId and the POD file from?** | Not specified | Section 3 lays out three integration scenarios; Glen picks the one that matches reality |
| **Where does the VB.NET app currently log its activity?** | Not specified | Section 7 shows how to wire the framework's audit log into VB.NET's existing logger if one exists; or to use the framework's audit alone |

When Glen points me at the actual VB.NET project, this document can be tightened. Until then it provides a working pattern that any reasonable VB.NET caller can adopt with minor adjustments.

---

## 2. The integration in one diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│  VB.NET application (Glen confirms which one)                            │
│                                                                            │
│  1. User clicks "Upload POD" on some screen (or batch process triggers)  │
│  2. App has: VectorLoadId, file path (or bytes), capture timestamp       │
│  3. App calls:                                                             │
│       VendorDispatcher.Instance.Dispatch(                                  │
│         New DocumentAvailableEvent With { ... })                          │
│  4. Returns immediately (fire-and-forget) — UI stays responsive          │
└────────────────────────────────────────┬─────────────────────────────────┘
                                         │
                                         ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Vendor.Common.VendorDispatcher                                           │
│  - Resolves shipper from VectorLoadId                                    │
│  - Finds ClientProfile for FourKites                                     │
│  - Calls FourKitesAdapter.DispatchAsync(event, profile)                  │
│  - Records VendorOutboundTransactions row                                │
└────────────────────────────────────────┬─────────────────────────────────┘
                                         │
                                         ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Vendor.FourKites.Adapter.FourKitesAdapter                                │
│  - Translates DocumentAvailableEvent → FK UploadDocumentRequest          │
│  - Maps DocumentType.ProofOfDelivery → FK code "DR"                      │
│  - Calls FourKitesClient.UploadDocumentAsync(...)                        │
│  - POSTs multipart/form-data to FK Documents API                         │
└──────────────────────────────────────────────────────────────────────────┘
```

The VB.NET caller is the leftmost box. Everything to the right of it is shared with OTR API.

---

## 3. Three integration scenarios

VB.NET apps come in many shapes. Pick the scenario that matches.

### 3.1 Scenario A — VB.NET app has its own DB connection and knows about loads

**Profile:** The VB.NET app is a desktop tool used by operations / scanning staff. It connects directly to Vector's SQL Server (or some equivalent) and looks up loads by VectorLoadId. When a POD is scanned/photographed/imported, the app already knows which load it's for.

**What VB.NET supplies:**
- VectorLoadId (already in app's data model)
- POD file (from local disk, from a scanner integration, from clipboard, etc.)
- Optional: timestamp the POD was captured

**Effort:** Minimum. Add one event dispatch call. No data lookup required.

### 3.2 Scenario B — VB.NET app needs to look up the load before uploading

**Profile:** The user enters a load number / BOL / reference, the app fetches load details (maybe via Vector FBS, maybe via OTR API), then attaches the POD.

**What VB.NET needs to do:**
1. Resolve the reference → VectorLoadId (same as it does today, presumably)
2. Dispatch `DocumentAvailableEvent`

**Effort:** Whatever existing lookup logic exists, plus one dispatch call.

### 3.3 Scenario C — VB.NET app gets POD events from another source (file watcher, queue, etc.)

**Profile:** A folder watcher monitors a directory; when a new PDF appears, the filename or metadata contains the load reference; app uploads automatically.

**What VB.NET needs to do:**
1. Parse the load reference from the trigger (filename, queue message, etc.)
2. Resolve to VectorLoadId if needed
3. Dispatch `DocumentAvailableEvent`

**Effort:** Same as Scenario B; the only new code is the dispatch.

**For this deliverable, the dispatch code is identical in all three scenarios.** Glen confirms which scenario applies; everything else is just where the dispatch fits into existing app flow.

---

## 4. Project setup

### 4.1 DLL references

Add references to the VB.NET project (Solution Explorer → right-click References → Add Reference → Browse):

| Reference | Path |
|---|---|
| `Vendor.Common.dll` | `<repo>\FourKitesIntegration\fourkites-code\Vendor.Common\bin\Release\Vendor.Common.dll` |
| `Vendor.FourKites.dll` | `<repo>\FourKitesIntegration\fourkites-code\Vendor.FourKites\bin\Release\Vendor.FourKites.dll` |
| `Newtonsoft.Json.dll` | comes via NuGet — install `Newtonsoft.Json 13.0.3` if not already present |

Set `<Private>True</Private>` on the Vendor references so the DLLs end up in the VB.NET app's bin folder at build time. This is the default for Browse-added references.

**If your VB.NET project doesn't already use NuGet for Newtonsoft.Json:** install via Tools → NuGet Package Manager → Manage NuGet Packages for Solution → search "Newtonsoft.Json" → install version 13.0.3 to match what Vendor.FourKites.dll expects.

### 4.2 App.config additions

Add to the VB.NET app's `App.config`:

```xml
<configuration>
  <configSections>
    <!-- existing sections unchanged -->
    <section name="vendorAdapters"
             type="Vendor.Common.Configuration.VendorAdaptersSection, Vendor.Common" />
  </configSections>

  <appSettings>
    <!-- existing settings unchanged -->

    <!-- Vendor dispatch framework -->
    <add key="VendorDispatch.Enabled" value="true" />
    <add key="VendorDispatch.AuditConnectionString"
         value="Server=10.10.9.10\SQLEXPRESS12;Database=VendorAPI_FK;Integrated Security=True" />
    <add key="VendorDispatch.FireAndForget" value="true" />
    <add key="VendorDispatch.SourceSystem" value="POD_App" />
  </appSettings>

  <vendorAdapters>
    <adapters>
      <add vendorName="FourKites"
           adapterType="Vendor.FourKites.Adapter.FourKitesAdapter, Vendor.FourKites"
           inboundProcessorType=""
           webhookValidatorType="" />
    </adapters>
  </vendorAdapters>
</configuration>
```

**Key differences from OTR API's Web.config:**

- `VendorDispatch.SourceSystem` is `"POD_App"` (or whatever the actual VB.NET app's name is). Stamped on every transaction so the audit log shows where uploads came from.
- `inboundProcessorType` and `webhookValidatorType` are empty — desktop apps don't host webhook endpoints. The framework gracefully ignores empty values; only the `adapterType` is required for outbound.

**On `Integrated Security`:** If the VB.NET app runs as a Windows user that doesn't have rights to the SQL server, you'll need SQL auth instead. See Deliverable #7 Section 8 for GRANT statements — the same apply, just with the VB.NET app's user/identity in place of the IIS app pool.

### 4.3 Imports statement

At the top of any VB.NET file that uses the framework:

```vbnet
Imports Vendor.Common
Imports Vendor.Common.Events
```

That's all the VB.NET code ever needs to import. **Notice: no `Imports Vendor.FourKites`.** The VB.NET app never references FK-specific types directly — that's the framework boundary.

---

## 5. The dispatch code — minimal version

The smallest possible POD upload, suitable for a quick prototype. Production code (Section 6) adds error handling and logging.

```vbnet
Imports Vendor.Common
Imports Vendor.Common.Events
Imports System.IO

Public Class PodUploader

    ''' <summary>
    ''' Uploads a POD for the given load. Returns immediately (fire-and-forget);
    ''' the actual FK call happens on a background thread.
    ''' </summary>
    ''' <param name="vectorLoadId">Vector's load identifier</param>
    ''' <param name="podFilePath">Full path to the POD file on disk</param>
    Public Shared Sub UploadPod(ByVal vectorLoadId As String, ByVal podFilePath As String)

        Dim fileBytes As Byte() = File.ReadAllBytes(podFilePath)
        Dim fileName As String = Path.GetFileName(podFilePath)
        Dim mimeType As String = GuessMimeType(podFilePath)

        Dim evt As New DocumentAvailableEvent() With {
            .VectorLoadId = vectorLoadId,
            .SourceSystem = "POD_App",
            .DocumentType = DocumentType.ProofOfDelivery,
            .FileName     = fileName,
            .MimeType     = mimeType,
            .Content      = fileBytes,
            .CapturedUtc  = DateTime.UtcNow
        }

        VendorDispatcher.Instance.Dispatch(evt)
    End Sub

    Private Shared Function GuessMimeType(ByVal path As String) As String
        Dim ext As String = Path.GetExtension(path).ToLowerInvariant()
        Select Case ext
            Case ".pdf"  : Return "application/pdf"
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".png"  : Return "image/png"
            Case ".tif", ".tiff" : Return "image/tiff"
            Case Else    : Return "application/octet-stream"
        End Select
    End Function

End Class
```

Total: about 30 lines. Most of it is parameter validation and MIME guessing. **The framework call itself is one line of substantive code.**

**Caller usage:**

```vbnet
' From a button click handler or background process:
PodUploader.UploadPod("LOAD12345", "C:\Scanned\pod-12345.pdf")
' Returns immediately. UI thread is unblocked.
```

---

## 6. The dispatch code — production version

Same logic, with the safety nets a real app needs.

```vbnet
Imports Vendor.Common
Imports Vendor.Common.Events
Imports System.IO

Public Class PodUploader

    Private Const MaxFileBytes As Integer = 25 * 1024 * 1024  ' 25 MB — FK's documented limit
    Private Shared ReadOnly AllowedExtensions As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        ".pdf", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    }

    ''' <summary>
    ''' Uploads a POD for the given load. Fire-and-forget by default
    ''' (per VendorDispatch.FireAndForget config). Returns a result object
    ''' indicating whether the dispatch was accepted (NOT whether FK ultimately accepted it —
    ''' that's recorded in VendorAPI_FK.VendorOutboundTransactions).
    ''' </summary>
    Public Shared Function UploadPod(
            ByVal vectorLoadId As String,
            ByVal podFilePath As String,
            Optional ByVal capturedUtc As DateTime? = Nothing) As PodUploadResult

        ' ─── Validation ───────────────────────────────────────────
        If String.IsNullOrWhiteSpace(vectorLoadId) Then
            Return PodUploadResult.Failed("VectorLoadId is required.")
        End If

        If String.IsNullOrWhiteSpace(podFilePath) OrElse Not File.Exists(podFilePath) Then
            Return PodUploadResult.Failed("POD file not found: " & podFilePath)
        End If

        Dim ext As String = Path.GetExtension(podFilePath)
        If Not AllowedExtensions.Contains(ext) Then
            Return PodUploadResult.Failed(
                "Unsupported file type: " & ext & ". Allowed: " & String.Join(", ", AllowedExtensions))
        End If

        Dim fileInfo As New FileInfo(podFilePath)
        If fileInfo.Length > MaxFileBytes Then
            Return PodUploadResult.Failed(
                String.Format("File too large: {0:N0} bytes. FK limit is {1:N0} bytes.",
                              fileInfo.Length, MaxFileBytes))
        End If

        If fileInfo.Length = 0 Then
            Return PodUploadResult.Failed("File is empty.")
        End If

        ' ─── Read file ────────────────────────────────────────────
        Dim fileBytes As Byte()
        Try
            fileBytes = File.ReadAllBytes(podFilePath)
        Catch ex As Exception
            Return PodUploadResult.Failed("Could not read file: " & ex.Message)
        End Try

        ' ─── Build event ──────────────────────────────────────────
        Dim evt As New DocumentAvailableEvent() With {
            .VectorLoadId = vectorLoadId.Trim(),
            .SourceSystem = "POD_App",
            .DocumentType = DocumentType.ProofOfDelivery,
            .FileName     = Path.GetFileName(podFilePath),
            .MimeType     = GuessMimeType(podFilePath),
            .Content      = fileBytes,
            .CapturedUtc  = If(capturedUtc, DateTime.UtcNow)
        }

        ' ─── Dispatch ─────────────────────────────────────────────
        Try
            VendorDispatcher.Instance.Dispatch(evt)
            Return PodUploadResult.Accepted(
                "POD queued for upload. Check VendorAPI_FK.VendorOutboundTransactions for outcome.")
        Catch ex As Exception
            ' VendorDispatcher.Dispatch should never throw, but defense in depth:
            Return PodUploadResult.Failed("Dispatcher failed: " & ex.Message)
        End Try

    End Function

    Private Shared Function GuessMimeType(ByVal path As String) As String
        Select Case Path.GetExtension(path).ToLowerInvariant()
            Case ".pdf"          : Return "application/pdf"
            Case ".jpg", ".jpeg" : Return "image/jpeg"
            Case ".png"          : Return "image/png"
            Case ".tif", ".tiff" : Return "image/tiff"
            Case Else            : Return "application/octet-stream"
        End Select
    End Function

End Class

''' <summary>Result of attempting to dispatch a POD upload.</summary>
Public Class PodUploadResult
    Public Property Success As Boolean
    Public Property Message As String

    Public Shared Function Accepted(ByVal message As String) As PodUploadResult
        Return New PodUploadResult With {.Success = True, .Message = message}
    End Function

    Public Shared Function Failed(ByVal message As String) As PodUploadResult
        Return New PodUploadResult With {.Success = False, .Message = message}
    End Function
End Class
```

**Caller usage:**

```vbnet
Private Sub btnUploadPod_Click(sender As Object, e As EventArgs) Handles btnUploadPod.Click
    Dim result = PodUploader.UploadPod(txtLoadId.Text, txtFilePath.Text)
    If result.Success Then
        lblStatus.Text = "POD queued."
        lblStatus.ForeColor = Color.Green
    Else
        lblStatus.Text = "POD upload rejected: " & result.Message
        lblStatus.ForeColor = Color.Red
    End If
End Sub
```

**Important distinction:** `result.Success = True` means the dispatcher accepted the event (file is valid, framework is healthy). It does NOT mean FK accepted the document — that happens asynchronously, and the outcome lives in `VendorAPI_FK.VendorOutboundTransactions` (queryable via the audit dashboard). See Section 8 for verification.

---

## 7. Logging within the VB.NET app

Two layers of logging happen:

**Layer 1 — framework audit (automatic).** Every dispatch creates a row in `VendorAPI_FK.VendorOutboundTransactions`. You get this for free.

**Layer 2 — VB.NET app's existing logger (if any).** If the VB.NET app already uses log4net / NLog / Windows Event Log / a custom logger, capture the `PodUploadResult` from `UploadPod` and log it through the existing channel. The framework doesn't interfere.

Example with log4net:

```vbnet
Private Shared ReadOnly Log As log4net.ILog = log4net.LogManager.GetLogger(GetType(SomeClass))

' ...

Dim result = PodUploader.UploadPod(loadId, podPath)
If result.Success Then
    Log.Info(String.Format("POD dispatched for Load {0}: {1}", loadId, podPath))
Else
    Log.Warn(String.Format("POD dispatch failed for Load {0}: {1}", loadId, result.Message))
End If
```

If you want the VB.NET app to know whether FK ultimately accepted the document (not just whether dispatch was queued), see Section 8 — that's a query against `VendorOutboundTransactions`, optionally polled by a background timer.

---

## 8. How to verify a POD upload worked

Three layers of verification, in order of immediacy:

### Layer 1 — Did the dispatch get queued?

`PodUploader.UploadPod` returns `Success = True`. Means: file passed validation, framework dispatcher accepted it.

### Layer 2 — Did FK accept the HTTP request? (ACK)

Within a few seconds, a row appears in `VendorAPI_FK.VendorOutboundTransactions`:

```sql
SELECT TransactionId, VendorName, EventTypeName, Status, HttpStatusCode,
       VendorRequestId, AckUtc, ResponseBody
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = 'LOAD12345'
  AND EventTypeName = 'DocumentAvailableEvent'
ORDER BY CreatedUtc DESC;
```

`Status = 'ACK'` and `HttpStatusCode = 202` means FK received the upload.

### Layer 3 — Did FK confirm the document is attached?

FK sends a webhook (`DOCUMENT_UPLOADED` or similar; exact event name varies by FK contract). When OTR API's webhook receiver (Deliverable #5) processes it, the row's status flips to `CONFIRMED`:

```sql
SELECT Status, ConfirmedUtc
FROM dbo.VendorOutboundTransactions
WHERE VectorLoadId = 'LOAD12345'
  AND EventTypeName = 'DocumentAvailableEvent';
```

`Status = 'CONFIRMED'` is the definitive "POD is in FK's system and attached to the load."

### Layer 4 (optional) — Visually confirm in FK's web UI

Log into FK's portal, find the load by Vector reference, navigate to Documents tab, confirm the POD is listed.

### A polling helper, if the VB.NET app wants to display "Confirmed" status to users

```vbnet
Imports System.Data.SqlClient

Public Class PodStatusChecker

    Private ReadOnly _connectionString As String

    Public Sub New(connectionString As String)
        _connectionString = connectionString
    End Sub

    ''' <summary>
    ''' Returns the most recent POD dispatch status for a load.
    ''' Possible values: "PENDING", "ACK", "CONFIRMED", "REJECTED",
    ''' "HTTP_FAIL", "TRANSPORT_FAIL", "RATE_LIMITED", or Nothing if no row.
    ''' </summary>
    Public Function GetMostRecentPodStatus(vectorLoadId As String) As String
        Using cn As New SqlConnection(_connectionString)
            cn.Open()
            Using cmd As New SqlCommand(
                "SELECT TOP 1 Status FROM dbo.VendorOutboundTransactions " &
                "WHERE VectorLoadId = @VectorLoadId " &
                "  AND EventTypeName = 'DocumentAvailableEvent' " &
                "ORDER BY CreatedUtc DESC", cn)

                cmd.Parameters.AddWithValue("@VectorLoadId", vectorLoadId)
                Dim result = cmd.ExecuteScalar()
                Return If(result Is Nothing OrElse result Is DBNull.Value,
                          Nothing,
                          CStr(result))
            End Using
        End Using
    End Function

End Class
```

Wire to a timer for 5-second polling if your UI wants live status updates. Most desktop POD workflows don't need this — the user uploaded, walked away, and the audit log handles forensics.

---

## 9. Threading and synchronization

VB.NET desktop apps often have UI thread concerns. Three things to know:

**1. `VendorDispatcher.Instance.Dispatch()` returns immediately when `FireAndForget = true`.** The actual HTTP call happens on a thread pool task. Safe to call from a UI event handler — won't block the form.

**2. If you set `FireAndForget = false`** and the dispatcher does block waiting for FK, calling it from the UI thread freezes the UI. Don't do this. Either keep `FireAndForget = true` or wrap the call in `Await Task.Run(...)` if you need to know synchronously when FK ACKed.

**3. The framework is thread-safe.** Calling `VendorDispatcher.Instance.Dispatch` from multiple threads simultaneously is fine — multiple uploads from multiple users / batch processes will all log correctly. The dispatcher serializes work to per-vendor rate-limit budgets internally; FK won't get DDOSed even if VB.NET sends 100 events at once.

---

## 10. Common VB.NET-specific gotchas

| # | Gotcha | Fix |
|---|---|---|
| 1 | `Imports Vendor.Common.Events` not found at compile time | Did you add the DLL reference? Check that `Vendor.Common.dll` is in References, set to `Copy Local = True`. |
| 2 | `Vendor.Common.dll` not found at runtime | Same as #1 but for deployment — the DLL must be in the same folder as the VB.NET .exe. `Copy Local = True` handles this at build, but deployment scripts may strip it. |
| 3 | `Newtonsoft.Json` version mismatch | The VB.NET app's existing Newtonsoft.Json reference might be 6.x or 10.x. Upgrade to 13.0.3 to match. If you can't upgrade for compatibility reasons, add a binding redirect in App.config. |
| 4 | App.config `<vendorAdapters>` section not recognized | Verify the `<configSections>` declaration is at the very top of App.config, before `<appSettings>`. .NET enforces this ordering. |
| 5 | "Could not load file or assembly Vendor.Common" with a specific version | Build configuration (Debug/Release) mismatch. Use Release builds of the Vendor DLLs for deployment. |
| 6 | `DocumentType.ProofOfDelivery` doesn't appear in IntelliSense | Old version of `Vendor.Common.dll`. Rebuild it after pulling the latest. |
| 7 | Dispatcher fires but no row appears in `VendorOutboundTransactions` | Check `VendorDispatch.AuditConnectionString` — does the VB.NET app's user have write access to `VendorAPI_FK`? See Deliverable #7 Section 8 for GRANT script. |
| 8 | POD uploads succeed for some loads but fail for others | Check `ClientProfiles.EnabledEvents` includes `DocumentAvailableEvent`. The seed script in #7 enables it; an admin may have edited it off. |
| 9 | `File.ReadAllBytes` throws `OutOfMemoryException` on huge PODs | Should never happen at ≤25MB on a 64-bit process. If the VB.NET app builds as 32-bit (x86), large files in memory can hit address space limits — change build to AnyCPU. |
| 10 | UI freezes during upload | `FireAndForget = false` somewhere — change to true, or wrap the call in `Await Task.Run(...)`. |

---

## 11. Smoke test plan

After wiring in the references and code:

| # | Test | Expected |
|---|---|---|
| 1 | Build the VB.NET project | Builds with no errors. Output bin folder contains `Vendor.Common.dll`, `Vendor.FourKites.dll`, `Newtonsoft.Json.dll`. |
| 2 | Run app; call `PodUploader.UploadPod` with a known-good test PDF and a VectorLoadId that exists in FK sandbox | Returns `Success = True` |
| 3 | Query `VendorAPI_FK.VendorOutboundTransactions` within 5 seconds | Row with `VendorName = 'FourKites'`, `EventTypeName = 'DocumentAvailableEvent'`, `Status = 'ACK'`, `HttpStatusCode = 202`, `SourceSystem = 'POD_App'` |
| 4 | Wait 1-2 minutes; re-query the same row | `Status = 'CONFIRMED'` (assuming FK emits a webhook for document upload — TBD with FK CSM) OR `Status` stays at `ACK` (if FK doesn't webhook docs; still success) |
| 5 | Visit FK sandbox web UI; find the test load; check Documents tab | POD file appears, type "DR" (Delivery Receipt) |
| 6 | Try uploading a too-large file (50MB) | `Success = False`, message explains size limit. No row in `VendorOutboundTransactions`. |
| 7 | Try uploading a file with a bad extension (.exe) | `Success = False`, message explains allowed types. No row. |
| 8 | Try uploading with an invalid VectorLoadId | Dispatch succeeds (framework doesn't validate load IDs). Row appears with `Status = 'HTTP_FAIL'` or `Status = 'REJECTED'` (FK rejects unknown loads). |
| 9 | Disable `VendorDispatch.Enabled` and retry | Upload silently no-ops (no row appears; `Success = True` still returned). |
| 10 | Run 20 uploads in rapid succession | All succeed. Audit log shows 20 rows. No rate-limit failures (FK is 60/min). |

Tests 3-5 are the end-to-end proof. Tests 6-9 verify error handling. Test 10 verifies the rate limit tracker handles concurrent dispatch correctly.

---

## 12. Summary of changes to the VB.NET app

The complete list of work:

| Change | Where | Lines |
|---|---|---|
| Add 2 DLL references | Project references | n/a (UI action) |
| Install Newtonsoft.Json 13.0.3 via NuGet | If not present | n/a (UI action) |
| Add `<configSections>` and `<appSettings>` blocks to App.config | App.config | ~15 lines |
| Add `<vendorAdapters>` section to App.config | App.config | ~5 lines |
| Add `PodUploader.vb` class | New file | ~100 lines (production version) |
| Wire `PodUploader.UploadPod()` call into existing upload trigger (button click, file watcher, etc.) | Existing form/class | ~5 lines |
| Optionally add `PodStatusChecker.vb` for live status display | New file | ~30 lines |

**Total: ~150 lines of new VB.NET code + small App.config additions.**

Zero changes to existing business logic. The PodUploader is a new path the existing app calls when it wants to send a POD.

---

## 13. Production checklist

Before going to production:

- [ ] Real FK API key in `VendorAPI_FK.ClientProfiles.ConfigJson` (not the placeholder from seed data)
- [ ] VB.NET app's runtime user has read/write to `VendorAPI_FK` (see Deliverable #7 GRANTs)
- [ ] The 10 smoke tests in Section 11 all pass
- [ ] Test with realistic POD file sizes (typical: 100KB-2MB scanned PDFs; verify no surprises)
- [ ] If the VB.NET app runs on multiple workstations, each one has the Vendor DLLs in its bin folder
- [ ] Binding redirects in App.config if any DLL version conflicts surface
- [ ] If POD volume is high (>100/day per workstation), confirm DB connection pooling is configured

---

## 14. Open items specific to this deliverable

| ID | Item | Resolution needed before |
|---|---|---|
| O-601 | Which VB.NET application is this? Project name, location, version of .NET, current dependencies | Building this for real |
| O-602 | Where does the VB.NET app obtain the VectorLoadId and POD file (Scenario A/B/C from Section 3)? | Wiring `PodUploader.UploadPod()` into existing app flow |
| O-603 | Does the app currently use a logger (log4net, NLog, custom)? | Section 7 logging wire-up |
| O-604 | Is the app deployed via ClickOnce, MSI, or copy-deploy? Affects DLL distribution | Production rollout |
| O-605 | Does the VB.NET app currently call OTR API for any other operations? If yes, consider keeping POD on the same channel for operational simplicity | Architectural — recommendation is "no, direct DLL is simpler" |

---

## 15. Done-when checklist

Mark this deliverable complete when:

- [ ] VB.NET project references `Vendor.Common.dll` and `Vendor.FourKites.dll`; builds clean
- [ ] App.config has `vendorAdapters` section and `VendorDispatch.*` settings
- [ ] `PodUploader.vb` added with production-version code
- [ ] PodUploader is called from existing app's POD trigger point
- [ ] All 10 smoke tests pass in dev/sandbox
- [ ] `grep -r "FourKites" .` in the VB.NET project source returns zero matches (apart from App.config's `vendorAdapters` registration and DLL hint paths in the .vbproj) — proves vendor-agnostic
- [ ] Production-credentialed `ClientProfile.ConfigJson` row exists in `VendorAPI_FK`
- [ ] First production POD upload appears in `VendorOutboundTransactions` with `Status = 'ACK'` within 24 hours of deployment

---

## 16. What this deliverable proves

After completion:

- A third caller (besides OTR API) uses the framework — proving the abstraction holds across web service + desktop callers
- VB.NET code references the same `Vendor.Common.VendorDispatcher` and the same `DocumentAvailableEvent` that OTR API uses
- POD uploads land in the same `VendorAPI_FK.VendorOutboundTransactions` table as everything else — one audit log, one query for "what happened with this load"
- Adding a vendor #2 that handles documents (project44 has a documents API too) requires zero VB.NET changes — the next vendor's adapter handles `DocumentAvailableEvent` automatically per its CanHandle() check
- A future customer evaluating the platform sees: "yes, the framework works from .NET desktop apps too, not just ASP.NET"

That last point matters for resale. "Plug into our platform from any .NET caller" is a stronger pitch than "plug into our web service."

---

*End of VB.NET POD Upload deliverable.*
