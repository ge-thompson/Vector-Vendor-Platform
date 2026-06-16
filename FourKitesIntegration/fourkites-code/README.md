# FourKites Integration for Vector FBS

C# integration that lets Vector FBS send load tracking updates to FourKites via API instead of (or alongside) EDI 214 messaging.

## Solution structure

```
FourKitesIntegration.sln
├── FourKitesIntegration.Core/              ← Class library (DTOs, FourKitesClient, EDI mapper)
├── FourKitesIntegration.OutboundService/   ← Windows Service hosting an internal HTTP API
├── FourKitesIntegration.WebhookReceiver/   ← Windows Service hosting the public webhook endpoint
├── FourKitesIntegration.SmokeTest/         ← Console app — first-API-key validation
└── SqlMigrations/                          ← Runnable .sql scripts
```

- **All projects target .NET Framework 4.8.1.**
- **Uses Newtonsoft.Json** (not System.Text.Json) for compatibility with the rest of Vector.
- **OWIN self-host** for both services — no IIS dependency.

## Architecture

```
                   Vector FBS                     OutboundService
                   (existing)                     (new)
                       │                              │
                       │ POST /api/fourkites/*        │
                       │ X-Internal-Auth header       │
                       ├─────────────────────────────►│ FourKitesClient
                       │                              │       │
                       │                              │       ▼
                       │                              │  api.fourkites.com
                       │                              │
                       │                              │
                                  SQL Server          │
                       ┌─────────────────────────────┐│
                       │ FourKitesOutboundTransactions││ ←── logged here
                       │ FourKitesInboundCallbacks    │
                       └─────────────────────────────┘
                                       ▲
                                       │
                                       │
                              WebhookReceiver       FourKites
                              (new)                 ───POST webhook──►
                              POST /fourkites/webhook
```

## Prerequisites

- Visual Studio 2019 (16.11) or later, OR Visual Studio 2022
- .NET Framework 4.8.1 Developer Pack installed
- SQL Server access (any recent version)
- FourKites staging API key (see Reference Doc Section 2.1)

## Build

```cmd
cd FourKitesIntegration
dotnet restore
dotnet build -c Release
```

Or open `FourKitesIntegration.sln` in Visual Studio and Build → Build Solution.

## First-time smoke test

1. **Run the SQL migrations** against your dev SQL Server:
   ```
   sqlcmd -S YOUR_SERVER -d VectorFBS -i SqlMigrations\02_OutboundTransactions.sql
   sqlcmd -S YOUR_SERVER -d VectorFBS -i SqlMigrations\03_InboundCallbacks.sql
   ```
   (You can skip 01 — that's the alternative queue-based design.)

2. **Edit `FourKitesIntegration.SmokeTest\App.config`**:
   - Set `FourKites.ApiKey` to your staging API key
   - Set `SmokeTest.BillToCode` to a real BillToCode registered in FourKites Connect (staging)
   - Set `SmokeTest.Carrier` to a real test carrier SCAC

3. **Build and run**:
   ```cmd
   dotnet run --project FourKitesIntegration.SmokeTest -c Release
   ```

4. **Verify**: log into app.fourkites.com (staging) and find the load with the test number printed in the console.

## Deploying the OutboundService as a Windows Service

1. **Configure** `FourKitesIntegration.OutboundService\App.config`:
   - `InternalListenUrl` — e.g. `http://localhost:8080/` (localhost-only is safest)
   - `InternalAuthToken` — generate a random 256-bit value, store in your vault
   - `FourKites.ApiKey` — load from your credential vault at startup (the config file is just a fallback)
   - `ConnectionString` — your Vector FBS SQL Server

2. **Publish**:
   ```cmd
   dotnet publish FourKitesIntegration.OutboundService -c Release -r win-x64 --self-contained false
   ```

3. **Copy** the published files to your target server (e.g. `C:\Services\FourKitesOutbound\`).

4. **Install as a Windows Service**:
   ```cmd
   sc create FourKitesOutbound binPath= "C:\Services\FourKitesOutbound\FourKitesIntegration.OutboundService.exe" start= auto
   sc description FourKitesOutbound "FourKites outbound API dispatcher for Vector FBS"
   sc start FourKitesOutbound
   ```

5. **Health check**: `curl http://localhost:8080/health` should return `{"status":"ok"...}`

## Deploying the WebhookReceiver

1. **Configure** `FourKitesIntegration.WebhookReceiver\App.config`:
   - `PublicListenUrl` — `http://+:8081/` for HTTP, or use a fronting reverse proxy for HTTPS
   - `WebhookAuthMode` — `ApiKey` or `Basic` (pick whichever FourKites configures on their side)
   - `WebhookAuthValue` / `WebhookBasicPassword` — secrets
   - `ConnectionString` — same SQL Server as OutboundService

2. **Bind HTTPS** (production): use `netsh http add sslcert` to bind your TLS cert to port 8081, OR put it behind IIS/nginx as a reverse proxy. Self-signed certs will be rejected by FourKites.

3. **Reserve the URL** for non-admin users to bind:
   ```cmd
   netsh http add urlacl url=http://+:8081/ user=DOMAIN\ServiceUser
   ```

4. **Install as a Windows Service**:
   ```cmd
   sc create FourKitesWebhookReceiver binPath= "C:\Services\FourKitesWebhook\FourKitesIntegration.WebhookReceiver.exe" start= auto
   sc start FourKitesWebhookReceiver
   ```

5. **Configure firewall**: open inbound TCP 8081 ONLY from the FourKites IP ranges. Get the ranges from your CSM (Reference Doc Section 2.1).

6. **Tell FourKites** (via CSM) the public URL: `https://fourkites-webhook.yourcompany.com/fourkites/webhook` and which auth credentials to send.

## Calling from Vector FBS

Add this helper somewhere accessible from your dispatch code:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

public static class FourKitesProxy
{
    private static readonly HttpClient _http = new HttpClient
    {
        BaseAddress = new Uri(ConfigurationManager.AppSettings["FourKitesOutboundUrl"])
    };
    private static readonly string _token = ConfigurationManager.AppSettings["FourKitesInternalAuthToken"];

    public static async Task<HttpResponseMessage> CreateShipment(object envelope)
    {
        var json = JsonConvert.SerializeObject(envelope);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/fourkites/create-shipment") { Content = content };
        req.Headers.Add("X-Internal-Auth", _token);
        return await _http.SendAsync(req);
    }
}
```

Wrap every call in try/catch — a FourKites failure must NEVER block a Vector dispatch event.

## Configuration security

- **NEVER** commit API keys, internal auth tokens, or webhook secrets to source control.
- The `App.config` files in this repo are templates with placeholder values.
- For production: load secrets from a credential vault (Windows Credential Manager, Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) at service startup, before instantiating `FourKitesClient`.
- The OutboundService internal listener should be bound to `localhost` only if Vector FBS runs on the same machine; otherwise restrict to your private VLAN.

## What's NOT yet built

These are deliberate gaps left for your team to fill based on local conventions:

- **Vector schema column names** — `04_VectorSchemaAdditions.sql` assumes table names `Shipper` and `Load`, and the LOAD_CREATION write-back in `WebhookCorrelator.StampFourKitesLoadIdOnVectorLoadAsync` assumes the same `[Load]` table with a `LoadId` column. Edit both before running.
- **Credential vault integration** — App.config values are placeholders.
- **Logging framework** — uses Windows EventLog by default. Wire Serilog/NLog if your team prefers structured logs.

## What IS now built

- **Webhook correlator** — runs as a background thread inside the WebhookReceiver service. Every 10 seconds (configurable) it claims a batch of unprocessed callbacks from `FourKitesInboundCallbacks`, matches each to an outbound transaction (by FourKitesLoadId first, then PrimaryReference fallback), updates the transaction status to CONFIRMED or REJECTED, and stamps FourKitesLoadId onto Vector's load table for LOAD_CREATION callbacks.

See `FourKites-Integration-Reference.docx` for the full reference and deployment plan.
