# Vector Vendor Integration

A vendor-agnostic integration platform for Vector's freight brokerage. Allows Vector's OTR API, VB.NET POD app, and (Phase 2) FBS to send status events to multiple shipper visibility platforms (FourKites today, project44 / Trimble / etc. tomorrow) without per-vendor code in the host applications.

## Project layout

```
_build/                   Framework + adapter source
  Vendor.Common/          Vendor-agnostic framework (events, contracts, dispatcher, correlator)
  Vendor.FourKites/       FourKites adapter — first vendor implementation

_test/                    Smoke test apps (4 console exes, 124 tests total)
  Vendor.Common.Smoke/                 23 unit tests
  Vendor.Common.Persistence.Smoke/     33 DB + correlator tests
  Vendor.Common.Dispatch.Smoke/        18 dispatcher tests
  Vendor.FourKites.Smoke/              50 FK adapter + webhook tests

_deliverables/            Design docs, decision log, SQL schema, worklog
  01_Strategy/            Master strategy + decision log (D-001 through D-029)
  ...                     (11 deliverables total)
  _worklog.md             Session-by-session billing + status

_OTR_API/                 The host web application that consumes the framework.
                          TruckTools tracking integration + short URL service +
                          (legacy, unused) InMotion + loadboard.

FourKitesIntegration/     Original wrong-endpoint DLL code (historical, do not modify)
```

## Architecture in one paragraph

Host applications call `VendorDispatcher.Instance.Dispatch(evt)` with a vendor-agnostic event (e.g. `LoadCreatedEvent`, `LocationReportedEvent`). The dispatcher looks up routing config in `dbo.ClientProfiles`, fans out to all matching vendor adapters (`IVendorAdapter`) in parallel, audits every outcome to `dbo.VendorOutboundTransactions`. For inbound webhooks, the controller hands raw payloads to `InboundCallbackRepository.UpsertAsync`, and the background `WebhookCorrelator` later matches them to outbound transactions via vendor-specific `IInboundEventProcessor` implementations.

Adding a new vendor is a new adapter project + one config row. The framework never changes.

## Build and test

Requires .NET Framework 4.8.1 and LocalDB (`(localdb)\mssqllocaldb`) with the `VendorAPI_FK` database deployed (scripts in `_deliverables/07_SQL_Schema/scripts/`).

```cmd
dotnet build VectorVendorIntegration.sln
```

To run any smoke test app, set it as the startup project and F5, or:

```cmd
cd _test\Vendor.FourKites.Smoke
dotnet run
```

Expect all 124 tests green.

## Key decisions

See `_deliverables/01_Strategy/01_Master_Strategy.md` for the full decision log (D-001 through D-029). The most consequential:

- **D-008** `VectorLoadId` is the universal correlation key across all events and vendors
- **D-018** Two-assembly split: `Vendor.Common` (framework) + `Vendor.FourKites` (adapter)
- **D-020** Framework-first build approach (resale intent reverses Rule-of-Three)
- **D-026** `VendorAPI_FK` database — "FK" means "FrameworK", not FourKites; the schema is vendor-agnostic by design
- **D-027** EDI 214 mapping is best-guess for Phase 1; refines after first production data
- **D-028** Webhook auth defaults to apikey-header scheme; basic/none also supported via config

## Open items

Tracked in `_deliverables/_worklog.md` (Session 4 section, "Open items"). Most need external input — FK CSM confirmation, Vector's load table location, etc.
