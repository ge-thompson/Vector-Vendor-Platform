# Vendor.Common Framework Design

**Document:** Deliverable #10 of 11 (promoted ahead of #4 per Glen's framework-first directive)
**Version:** 1.0
**Date:** May 26, 2026
**Owner:** Glen Thompson (executor); Claude (designer)
**Prerequisites:** Master Strategy doc (D-020 framework-first decision)

---

## 0. Purpose

This document designs the **vendor-agnostic framework** that sits between caller applications (OTR API, FBS, VB.NET POD app) and vendor-specific implementations (Vendor.FourKites today; future Vendor.Project44, Vendor.Macropoint, etc.).

The framework's job is to make this true:

> **Adding a new vendor never requires changing OTR API, FBS, the VB.NET app, the database schema, or the dispatcher. You write one new adapter class, add one config row, and you're done.**

If we get this right, that statement holds. If we get it wrong, we discover it when vendor #2 arrives and we have to refactor.

---

## 1. The shape of the framework

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Caller applications (OTR API, FBS, VB.NET POD app)                      │
│                                                                            │
│  They speak ONLY in vendor-agnostic events:                              │
│      VendorDispatcher.Instance.Dispatch(new LoadAssignedEvent { ... })   │
└────────────────────────────────────────┬─────────────────────────────────┘
                                         │ vendor-agnostic event
                                         ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Vendor.Common                                                            │
│                                                                            │
│  VendorDispatcher                                                         │
│    ├── reads ClientProfile (which vendors for this shipper?)             │
│    ├── for each configured vendor:                                       │
│    │     ├── get adapter from VendorAdapterRegistry                      │
│    │     ├── adapter.Dispatch(event)                                     │
│    │     └── record outcome to VendorOutboundTransactions                │
│    └── return (void; errors logged, never propagate)                     │
│                                                                            │
│  Internal event types: LoadAssignedEvent, LocationReportedEvent, etc.    │
│  Adapter contract: IVendorAdapter                                        │
└────────────────────────────────────────┬─────────────────────────────────┘
                                         │ vendor-specific payload
                                         ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Vendor.FourKites    Vendor.Project44 (future)    Vendor.X (future)      │
│                                                                            │
│  FourKitesAdapter    Project44Adapter             VendorXAdapter         │
│    : IVendorAdapter    : IVendorAdapter             : IVendorAdapter     │
│                                                                            │
│  Each adapter:                                                            │
│    - translates events into its vendor's payload shape                   │
│    - calls its vendor's API client                                       │
│    - returns a VendorOperationResult                                     │
└──────────────────────────────────────────────────────────────────────────┘
                                         │
                                         ▼
                                 vendor's REST API
```

The clean separation:

| Layer | Knows about | Doesn't know about |
|---|---|---|
| Callers (OTR API, FBS, VB.NET) | Internal events | Any vendor's API, payload shapes, auth |
| Vendor.Common | Internal events, dispatcher, adapter contract, ClientProfile | Any specific vendor |
| Vendor adapters | Their own vendor's API + internal events | Other vendors, callers |

---

## 2. Internal event types — the vendor-agnostic vocabulary

This is the foundation. Every caller speaks these events; every adapter translates from them.

### 2.1 Design principles for events

Three rules:

1. **Past tense, factual.** Events describe what *happened*, not what should happen. `LocationReportedEvent` (a GPS update was received), not `UpdateLocation` (a command). Callers don't say "do this"; they say "this happened, do whatever you do with that."

2. **Carry only what every plausible vendor would need.** Resist the urge to include FK-specific fields. If FK needs `billToCode` and project44 doesn't, that's vendor configuration, not event data.

3. **Always carry `VectorLoadId`.** Every event. No exceptions. It's how the framework correlates anything.

### 2.2 The events for Phase 1

Five events cover the OTR API + VB.NET POD use cases:

```csharp
namespace Vendor.Common.Events
{
    /// <summary>Base class — all events carry VectorLoadId and a timestamp.</summary>
    public abstract class VendorEvent
    {
        public string VectorLoadId { get; set; }
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
        public string SourceSystem { get; set; }  // "OTR_API", "VectorFBS", "POD_App"
    }

    /// <summary>A new load was created in the source system and is now active.</summary>
    public class LoadCreatedEvent : VendorEvent
    {
        public StopInfo Origin { get; set; }
        public StopInfo Destination { get; set; }
        public List<StopInfo> Stops { get; set; }   // includes origin and destination
        public string Mode { get; set; }            // "TL", "LTL", "INTERMODAL"
        public string EquipmentType { get; set; }   // "Dry Van", "Reefer", etc.
        public decimal? Weight { get; set; }
        public string WeightUnit { get; set; }      // "LB", "KG"
        public List<ReferenceNumber> References { get; set; }
    }

    /// <summary>A carrier/driver/equipment was assigned to a load (or reassigned).</summary>
    public class LoadAssignedEvent : VendorEvent
    {
        public CarrierInfo Carrier { get; set; }
        public DriverInfo Driver { get; set; }
        public EquipmentInfo Equipment { get; set; }
    }

    /// <summary>A GPS position was reported for a load.</summary>
    public class LocationReportedEvent : VendorEvent
    {
        public string Latitude { get; set; }        // string per FK convention; works for all
        public string Longitude { get; set; }
        public DateTime LocatedAtUtc { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public double? SpeedMph { get; set; }
        public double? Heading { get; set; }
    }

    /// <summary>A status event happened (arrived, departed, etc.).</summary>
    public class LoadStatusEvent : VendorEvent
    {
        public LoadStatusType StatusType { get; set; }   // see enum below
        public DateTime StatusTimeUtc { get; set; }
        public StopInfo AtStop { get; set; }              // which stop, if applicable
        public string SourceStatusCode { get; set; }      // raw code from upstream (TT, etc.)
        public string SourceStatusDescription { get; set; }
    }

    /// <summary>Load is no longer being tracked (cancelled, completed final-tracking).</summary>
    public class LoadTrackingStoppedEvent : VendorEvent
    {
        public string Reason { get; set; }   // "CANCELLED", "DELIVERED", "DISPATCHER_STOPPED"
    }

    /// <summary>A document (POD, BOL, etc.) is ready to attach.</summary>
    public class DocumentAvailableEvent : VendorEvent
    {
        public DocumentType DocumentType { get; set; }    // see enum below
        public string FileName { get; set; }
        public string MimeType { get; set; }
        public byte[] Content { get; set; }
        public DateTime? CapturedUtc { get; set; }
    }

    /// <summary>Generic — events that don't fit elsewhere.</summary>
    public class GenericLoadEvent : VendorEvent
    {
        public string EventName { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }

    // ─── Supporting types ──────────────────────────────────────────────

    public enum LoadStatusType
    {
        Dispatched,
        ArrivedAtPickup,
        DepartedPickup,
        InTransit,
        ArrivedAtDelivery,
        DepartedDelivery,
        Delivered,
        Exception,    // generic — see SourceStatusCode for details
        Other         // genuinely doesn't fit; rely on SourceStatusCode
    }

    public enum DocumentType
    {
        ProofOfDelivery,
        BillOfLading,
        RateConfirmation,
        WeighSlip,
        Other
    }

    public class StopInfo
    {
        public int? SequenceNumber { get; set; }
        public StopRole Role { get; set; }   // Pickup, Delivery, Intermediate
        public string Name { get; set; }
        public string AddressLine1 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
        public DateTime? ScheduledArrivalUtc { get; set; }
        public DateTime? ScheduledDepartureUtc { get; set; }
        public List<ReferenceNumber> References { get; set; }
    }

    public enum StopRole { Pickup, Delivery, Intermediate }

    public class CarrierInfo
    {
        public string Scac { get; set; }
        public string Name { get; set; }
        public string McNumber { get; set; }
        public string DotNumber { get; set; }
    }

    public class DriverInfo
    {
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
    }

    public class EquipmentInfo
    {
        public string TruckNumber { get; set; }
        public string TrailerNumber { get; set; }
        public string Vin { get; set; }
        public string LicensePlate { get; set; }
    }

    public class ReferenceNumber
    {
        public string Type { get; set; }   // "BOL", "PO", "ShipperRef", etc.
        public string Value { get; set; }
    }
}
```

### 2.3 What I left out and why

| Considered | Verdict | Reason |
|---|---|---|
| `EtaUpdatedEvent` | Skip for Phase 1 | OTR API doesn't compute ETA; not needed yet |
| `TemperatureReportedEvent` | Skip for Phase 1 | Reefer-only; add when first reefer load runs |
| `MessageReceivedEvent` (driver chat) | Skip | Phase 3+ territory |
| `LoadTenderedEvent` | Skip | This is upstream of OTR API's role |
| Per-event vendor metadata bag | Skip — keep events clean | If a vendor needs extra data, the adapter pulls it from elsewhere |

### 2.4 Pressure test — does this work for project44?

I checked each event against what I know of project44's API surface:

| Event | FourKites maps to | Project44 maps to | Works for both? |
|---|---|---|---|
| `LoadCreatedEvent` | CreateShipment | createShipment (theirs is also a thing) | ✅ |
| `LoadAssignedEvent` | DispatcherUpdate.AssignmentUpdate | shipment update with driver/equipment | ✅ |
| `LocationReportedEvent` | DispatcherUpdate.LocationUpdate | shipment.position | ✅ |
| `LoadStatusEvent` | DispatcherUpdate.EventUpdate (EDI 214 code) | shipment.statusUpdate (their own enum) | ✅ (adapter does the code mapping) |
| `LoadTrackingStoppedEvent` | LoadInfoUpdate(StopTracking=true) | terminate shipment | ✅ |
| `DocumentAvailableEvent` | Documents API | document attachment endpoint | ✅ |

Good — the events are at the right level of abstraction. Vendor-specific quirks (EDI 214 codes, FK's IdentifierKeys, project44's authentication) stay in the adapter.

---

## 3. The adapter contract — IVendorAdapter

The adapter is what makes one vendor pluggable. It implements this interface:

```csharp
namespace Vendor.Common.Abstractions
{
    public interface IVendorAdapter
    {
        /// <summary>"FourKites", "Project44", etc. — used for logging and ClientProfile matching.</summary>
        string VendorName { get; }

        /// <summary>
        /// True if this adapter handles the given event type. Adapters can refuse events
        /// they don't support — the dispatcher logs the skip and moves on.
        /// </summary>
        bool CanHandle(VendorEvent evt);

        /// <summary>
        /// Translate the event and send to the vendor's API. Implementations must:
        ///  - never throw (catch internally; return Failed result)
        ///  - respect the vendor's rate limit
        ///  - return enough metadata for the dispatcher to log VendorOutboundTransactions
        /// </summary>
        Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt,
            ClientProfile profile,
            CancellationToken cancellationToken = default);
    }

    public class VendorOperationResult
    {
        public bool Success { get; set; }
        public int? HttpStatusCode { get; set; }
        public string VendorRequestId { get; set; }   // FK's requestId, P44's tracking id, etc.
        public string RequestPayloadJson { get; set; }
        public string ResponseBodyJson { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorCategory { get; set; }     // "Transient", "Permanent", "RateLimit"
        public TimeSpan Duration { get; set; }
    }
}
```

**Three rules adapters must follow:**

1. **Never throw out of `DispatchAsync`.** Catch internally. Return a `VendorOperationResult` with `Success = false` and details. The dispatcher relies on this to keep going for the next vendor in the routing list.

2. **Self-rate-limit.** Each vendor has its own rate limit; the adapter is responsible for enforcing it. FK = 60/min; P44 might differ. The dispatcher does NOT coordinate cross-vendor rate limits.

3. **Always populate `RequestPayloadJson` and `ResponseBodyJson`.** These get persisted to the audit log; they're how Glen answers "what did we actually send and what came back."

---

## 4. ClientProfile — config-driven routing

A `ClientProfile` row says:

> "For shipper `<ShipperCode>`, route these `<EventTypes>` to vendor `<VendorName>` using this `<configuration bag>`."

```csharp
namespace Vendor.Common.Configuration
{
    public class ClientProfile
    {
        public long ProfileId { get; set; }
        public string ShipperCode { get; set; }      // e.g. "ACME", "VECTOR_DEFAULT"
        public string VendorName { get; set; }       // "FourKites", "Project44"
        public bool IsActive { get; set; }
        public string EnabledEvents { get; set; }    // CSV: "LoadCreated,LoadAssigned,LocationReported,..."
        public string ConfigJson { get; set; }       // vendor-specific blob — API key (encrypted), base URL, etc.
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
```

**Shape rationale:**

- `ShipperCode` lets you route differently per shipper. Phase 1 will have one row with `ShipperCode = "VECTOR_DEFAULT"` (acts as fallback for everything).
- `EnabledEvents` is a CSV string (not a separate table) — keeps queries simple, easy to read in admin tools, and the cardinality is low (~10 event types max).
- `ConfigJson` is a vendor-specific dictionary — FK needs `apiKey`, `billToCode`, `baseUrl`; P44 needs different things. Each adapter knows how to parse its own ConfigJson.
- `IsActive` lets you disable a vendor without deleting the row (audit trail, easy re-enable).

### 4.1 Sample rows

```json
{
  "ProfileId": 1,
  "ShipperCode": "VECTOR_DEFAULT",
  "VendorName": "FourKites",
  "IsActive": true,
  "EnabledEvents": "LoadCreated,LoadAssigned,LocationReported,LoadStatus,LoadTrackingStopped,DocumentAvailable",
  "ConfigJson": "{\"apiKey\":\"<encrypted>\",\"billToCode\":\"VECTOR-001\",\"baseUrl\":\"https://api.fourkites.com\",\"timeoutSeconds\":15}"
}
```

```json
{
  "ProfileId": 2,
  "ShipperCode": "ACME",
  "VendorName": "Project44",
  "IsActive": true,
  "EnabledEvents": "LocationReported,LoadStatus",
  "ConfigJson": "{\"oauthClientId\":\"...\",\"oauthClientSecret\":\"<encrypted>\",\"baseUrl\":\"https://na12.api.project44.com\"}"
}
```

These two rows say: every load goes to FourKites for everything; loads for ACME *also* go to project44 for location and status only.

### 4.2 Routing semantics

Given an event with `VectorLoadId` X and an event type Y:

1. Look up the load's shipper code (cross-reference — see Section 6)
2. Find all `ClientProfile` rows where:
 - `ShipperCode = <load's shipper>` OR `ShipperCode = 'VECTOR_DEFAULT'`
 - `IsActive = true`
 - `EnabledEvents` contains Y
3. For each match: call that vendor's adapter

A load can fan out to multiple vendors. A vendor sees only events the profile enables for that shipper.

**Conflict resolution:** if both a specific shipper row AND a `VECTOR_DEFAULT` row match, **both** vendors receive the event. The default is a *floor*, not a fallback — it's how you say "FK always gets everything, AND ACME loads also go to project44." If you want the specific row to *override* the default, mark `VECTOR_DEFAULT` inactive for that shipper-vendor pair (a future enhancement; not needed Phase 1).

---

## 5. VendorDispatcher — the routing engine

The dispatcher is the one place callers interact with. Here's the full design:

```csharp
namespace Vendor.Common
{
    public class VendorDispatcher
    {
        private static readonly Lazy<VendorDispatcher> _instance =
            new Lazy<VendorDispatcher>(() => new VendorDispatcher());
        public static VendorDispatcher Instance => _instance.Value;

        private readonly ClientProfileRepository _profiles;
        private readonly VendorAdapterRegistry _adapters;
        private readonly OutboundTransactionRepository _audit;
        private readonly LoadShipperResolver _shipperResolver;
        private readonly bool _enabled;
        private readonly bool _fireAndForget;

        private VendorDispatcher()
        {
            _enabled = bool.Parse(ConfigurationManager.AppSettings["VendorDispatch.Enabled"] ?? "false");
            if (!_enabled) return;

            var connString = ConfigurationManager.AppSettings["VendorDispatch.AuditConnectionString"];
            _profiles        = new ClientProfileRepository(connString);
            _audit           = new OutboundTransactionRepository(connString);
            _shipperResolver = new LoadShipperResolver(connString);
            _adapters        = VendorAdapterRegistry.LoadFromConfig();  // see Section 5.3
            _fireAndForget   = bool.Parse(ConfigurationManager.AppSettings["VendorDispatch.FireAndForget"] ?? "true");
        }

        /// <summary>
        /// Caller's only entry point. Dispatches the event to all configured vendors
        /// for the load's shipper. Never throws.
        /// </summary>
        public void Dispatch(VendorEvent evt)
        {
            if (!_enabled || evt == null) return;
            if (_fireAndForget)
            {
                Task.Run(async () =>
                {
                    try { await DispatchInternalAsync(evt).ConfigureAwait(false); }
                    catch (Exception ex) { LogError(ex, evt); }
                });
            }
            else
            {
                try { Task.Run(() => DispatchInternalAsync(evt)).Wait(); }
                catch (Exception ex) { LogError(ex, evt); }
            }
        }

        /// <summary>Synchronous version for callers that need to await completion.</summary>
        public Task DispatchAsync(VendorEvent evt) => DispatchInternalAsync(evt);

        private async Task DispatchInternalAsync(VendorEvent evt)
        {
            // 1. Find the load's shipper
            var shipperCode = _shipperResolver.GetShipperFor(evt.VectorLoadId)
                              ?? "VECTOR_DEFAULT";

            // 2. Find all matching profiles
            var profiles = _profiles.FindRouting(
                shipperCode: shipperCode,
                eventType: evt.GetType().Name);

            if (profiles.Count == 0)
            {
                // Nothing configured — log and move on
                _audit.InsertSkipped(evt, reason: "No matching ClientProfile");
                return;
            }

            // 3. For each profile, call the vendor's adapter
            foreach (var profile in profiles)
            {
                var adapter = _adapters.GetByVendorName(profile.VendorName);
                if (adapter == null)
                {
                    _audit.InsertSkipped(evt, reason: $"No adapter registered for {profile.VendorName}");
                    continue;
                }
                if (!adapter.CanHandle(evt))
                {
                    _audit.InsertSkipped(evt, reason: $"{profile.VendorName} adapter declined event type");
                    continue;
                }

                // 4. Log "pending", dispatch, log outcome
                var txId = _audit.InsertPending(evt, profile.VendorName);
                try
                {
                    var result = await adapter.DispatchAsync(evt, profile).ConfigureAwait(false);
                    _audit.RecordOutcome(txId, result);
                }
                catch (Exception ex)
                {
                    // Adapters should never throw, but defense in depth
                    _audit.RecordError(txId, ex);
                }
            }
        }

        private void LogError(Exception ex, VendorEvent evt)
        {
            try
            {
                _audit.InsertDispatcherError(evt, ex);
            }
            catch { /* never let audit failure break things */ }
        }
    }
}
```

### 5.1 What the dispatcher does NOT do

Deliberate omissions:

- **No business logic.** It routes; it doesn't decide what to do.
- **No cross-vendor coordination.** Each vendor's adapter is independent. If vendor A fails, vendor B still gets called.
- **No retry policy.** That lives in the adapters (vendor-specific backoff makes sense; cross-vendor backoff doesn't).
- **No event transformation.** The event arrives as the caller sent it; the adapter transforms.
- **No webhook handling.** That's inbound; the dispatcher is outbound only.

### 5.2 What about the `Vendor.FourKites` adapter?

```csharp
namespace Vendor.FourKites.Adapter
{
    public class FourKitesAdapter : IVendorAdapter
    {
        public string VendorName => "FourKites";

        public bool CanHandle(VendorEvent evt) => evt is LoadCreatedEvent
                                              || evt is LoadAssignedEvent
                                              || evt is LocationReportedEvent
                                              || evt is LoadStatusEvent
                                              || evt is LoadTrackingStoppedEvent
                                              || evt is DocumentAvailableEvent;

        public async Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt, ClientProfile profile, CancellationToken ct = default)
        {
            var config = FourKitesConfig.ParseJson(profile.ConfigJson);
            var client = FourKitesClientCache.GetOrCreate(config);
            try
            {
                switch (evt)
                {
                    case LoadCreatedEvent e:
                        return await DispatchLoadCreated(e, client, config, ct).ConfigureAwait(false);
                    case LoadAssignedEvent e:
                        return await DispatchLoadAssigned(e, client, config, ct).ConfigureAwait(false);
                    case LocationReportedEvent e:
                        return await DispatchLocationReported(e, client, config, ct).ConfigureAwait(false);
                    case LoadStatusEvent e:
                        return await DispatchLoadStatus(e, client, config, ct).ConfigureAwait(false);
                    case LoadTrackingStoppedEvent e:
                        return await DispatchTrackingStopped(e, client, config, ct).ConfigureAwait(false);
                    case DocumentAvailableEvent e:
                        return await DispatchDocument(e, client, config, ct).ConfigureAwait(false);
                    default:
                        return VendorOperationResult.Skipped("Unhandled event type");
                }
            }
            catch (Exception ex)
            {
                return VendorOperationResult.Failed(ex);
            }
        }

        // Each Dispatch* method translates the event to FK's payload and calls the client.
        // The Edi214Mapper, IdentifierKey shaping, FK time formatting, etc.,
        // ALL live in these private methods — never leak out.

        private async Task<VendorOperationResult> DispatchLoadStatus(
            LoadStatusEvent e, FourKitesClient client, FourKitesConfig config, CancellationToken ct)
        {
            var ediCode = StatusTypeToEdi214(e.StatusType, e.SourceStatusCode);
            if (ediCode == null) return VendorOperationResult.Skipped("Status not mapped");

            var batch = new DispatcherBatch
            {
                BillToCode = config.BillToCode,
                LoadUpdate = new List<LoadUpdate>
                {
                    new LoadUpdate
                    {
                        IdentifierKeys = new[] { new IdentifierKey {
                            Identifier = e.VectorLoadId, IdentifierType = "loadNumber" } },
                        EventUpdate = new EventUpdate
                        {
                            StatusCode = ediCode,
                            StatusDescription = e.SourceStatusDescription,
                            EventTimestamp = FourKitesTime.ToIso8601(e.StatusTimeUtc),
                            City = e.AtStop?.City,
                            State = e.AtStop?.State
                        }
                    }
                }
            };
            var response = await client.SendDispatcherUpdateAsync(batch, ct).ConfigureAwait(false);
            return VendorOperationResult.From(response, batch);
        }

        // ... similar methods for the other events
    }
}
```

The adapter is where ALL the FK-specific knowledge lives:
- EDI 214 code mapping
- IdentifierKey shape
- BillToCode handling
- ISO 8601 time formatting per FK convention
- API key auth
- FK's rate limit (60/min)

None of that knowledge escapes into Vendor.Common or into caller code.

### 5.3 VendorAdapterRegistry — how adapters get discovered

```csharp
namespace Vendor.Common
{
    public class VendorAdapterRegistry
    {
        private readonly Dictionary<string, IVendorAdapter> _byName =
            new Dictionary<string, IVendorAdapter>(StringComparer.OrdinalIgnoreCase);

        public IVendorAdapter GetByVendorName(string vendorName)
            => _byName.TryGetValue(vendorName, out var a) ? a : null;

        public void Register(IVendorAdapter adapter) => _byName[adapter.VendorName] = adapter;

        public static VendorAdapterRegistry LoadFromConfig()
        {
            // Reads <vendorAdapters> section in App/Web.config:
            //   <vendorAdapters>
            //     <add vendorName="FourKites" assembly="Vendor.FourKites"
            //          type="Vendor.FourKites.Adapter.FourKitesAdapter" />
            //     <add vendorName="Project44" assembly="Vendor.Project44"
            //          type="Vendor.Project44.Adapter.Project44Adapter" />
            //   </vendorAdapters>
            var registry = new VendorAdapterRegistry();
            var section = (VendorAdaptersSection)ConfigurationManager.GetSection("vendorAdapters");
            foreach (VendorAdapterElement el in section.Adapters)
            {
                var assembly = System.Reflection.Assembly.Load(el.Assembly);
                var type     = assembly.GetType(el.Type);
                var adapter  = (IVendorAdapter)Activator.CreateInstance(type);
                registry.Register(adapter);
            }
            return registry;
        }
    }
}
```

**Why config-driven, not hardcoded?** Because the resale story requires it. A customer using this platform doesn't recompile to add their adapter — they drop the DLL in `bin\`, add a config line, restart.

---

## 6. LoadShipperResolver — finding the shipper

The dispatcher needs to know "what shipper does this load belong to?" to look up ClientProfiles. That mapping lives somewhere in Vector's data — needs to be resolved at dispatch time.

For Phase 1, the simplest implementation:

```csharp
namespace Vendor.Common
{
    public class LoadShipperResolver
    {
        private readonly string _connectionString;
        private readonly MemoryCache _cache = new MemoryCache(...);  // brief TTL

        public string GetShipperFor(string vectorLoadId)
        {
            // 1. Check cache (loads are referenced multiple times per dispatch cycle)
            // 2. Query: SELECT ShipperCode FROM VectorOTR.dbo.Loads WHERE VectorID = @id
            //    (or wherever shipper info lives — Glen confirms the source)
            // 3. Return null if not found (dispatcher uses VECTOR_DEFAULT)
        }
    }
}
```

**Open item O-501:** Where in Vector's databases does shipper code live for a load? This is the only lookup the framework needs to function; needs Glen's input.

For Phase 1 we can ship with this resolver returning `"VECTOR_DEFAULT"` for all loads — single profile row routes everything to FK — and add real resolution when the multi-shipper case arrives. This is fine for the first deployment.

---

## 7. Where each piece lives (project layout)

```
Vendor.Common\
├── Vendor.Common.csproj
├── Abstractions\
│   ├── IVendorAdapter.cs             ← THE contract
│   ├── IVendorClient.cs              ← marker, unchanged from D-2 design
│   ├── IRateLimitTracker.cs
│   └── IWebhookSignatureValidator.cs
├── Events\
│   ├── VendorEvent.cs                ← base class
│   ├── LoadCreatedEvent.cs
│   ├── LoadAssignedEvent.cs
│   ├── LocationReportedEvent.cs
│   ├── LoadStatusEvent.cs
│   ├── LoadTrackingStoppedEvent.cs
│   ├── DocumentAvailableEvent.cs
│   ├── GenericLoadEvent.cs
│   ├── SupportingTypes.cs            ← StopInfo, CarrierInfo, DriverInfo, etc.
│   └── Enums.cs                      ← LoadStatusType, DocumentType, StopRole
├── Configuration\
│   ├── ClientProfile.cs
│   ├── ClientProfileRepository.cs
│   ├── VendorAdaptersSection.cs      ← Web.config custom section
│   └── VendorAdapterElement.cs
├── Dispatch\
│   ├── VendorDispatcher.cs           ← the engine
│   ├── VendorAdapterRegistry.cs      ← discovers adapters from config
│   └── LoadShipperResolver.cs        ← VectorLoadId → ShipperCode
├── Persistence\
│   ├── OutboundTransaction.cs        ← (existing, has VendorName)
│   ├── OutboundTransactionRepository.cs
│   ├── InboundCallback.cs
│   ├── InboundCallbackRepository.cs
│   └── WebhookCorrelator.cs
├── Errors\
│   ├── ErrorClassification.cs
│   └── VendorOperationResult.cs

Vendor.FourKites\
├── Vendor.FourKites.csproj
├── Adapter\
│   ├── FourKitesAdapter.cs           ← IMPLEMENTS IVendorAdapter
│   ├── FourKitesConfig.cs            ← parses ConfigJson
│   ├── FourKitesClientCache.cs       ← per-config client singleton
│   └── EventTranslators\
│       ├── LoadCreatedTranslator.cs
│       ├── LoadAssignedTranslator.cs
│       ├── LocationReportedTranslator.cs
│       ├── LoadStatusTranslator.cs
│       ├── LoadTrackingStoppedTranslator.cs
│       └── DocumentAvailableTranslator.cs
├── Client\
│   ├── FourKitesClient.cs            ← (unchanged from existing)
│   └── ...
├── Models\... (unchanged)
├── Mapping\
│   ├── Edi214Mapper.cs               ← (existing) — FK's status code logic
│   └── TruckToolsStatusToEdi214.cs   ← MOVED from OTR API!
└── Webhooks\... (unchanged)
```

### 7.1 Important: TruckToolsStatusToEdi214 moves OUT of OTR API

In Deliverable #4 as I originally wrote it, the TruckTools-status-code-to-EDI-214 mapper lived in OTR API. **That was wrong.** OTR API shouldn't know what EDI 214 is.

In the new design:
- OTR API converts TruckTools status codes to `LoadStatusType` enum values + carries the raw code as `SourceStatusCode`
- The FourKitesAdapter's translator decides EDI 214 codes from `LoadStatusType` (with `SourceStatusCode` as a fallback for codes that don't fit the enum cleanly)

This means project44's adapter would convert the SAME `LoadStatusType` to project44's status enum, without ever touching EDI 214. The OTR API code is now genuinely vendor-agnostic.

---

## 8. What OTR API code looks like with the framework

This shows what Deliverable #4 will look like rewritten. Just for context — the actual rewrite is the next deliverable.

```csharp
// In TruckerToolsTrackingController.SendStatus:
// After existing TT persistence...

VendorDispatcher.Instance.Dispatch(new LocationReportedEvent
{
    VectorLoadId = lc.loadNumber,
    SourceSystem = "OTR_API",
    Latitude = lc.latestLocation?.lat,
    Longitude = lc.latestLocation?.lon,
    LocatedAtUtc = ParseTimestamp(lc.latestLocation?.timeStamp),
    City = lc.latestLocation?.city,
    State = lc.latestLocation?.state
});

if (lc.latestStatus != null)
{
    VendorDispatcher.Instance.Dispatch(new LoadStatusEvent
    {
        VectorLoadId = lc.loadNumber,
        SourceSystem = "OTR_API",
        StatusType = MapTruckToolsStatusToType(lc.latestStatus.code),
        SourceStatusCode = lc.latestStatus.code,
        SourceStatusDescription = lc.latestStatus.name,
        StatusTimeUtc = ParseTimestamp(lc.latestStatus.timeStamp),
        AtStop = lc.latestStatus.location != null ? new StopInfo {
            City = lc.latestStatus.location.city,
            State = lc.latestStatus.location.state
        } : null
    });
}
```

Notice:
- Zero mention of "FourKites" or "FK"
- Zero mention of FK's specific concepts (BillToCode, IdentifierKey, EDI 214, DispatcherBatch)
- If you delete the `Vendor.FourKites` DLL and replace it with `Vendor.Project44`, this code does not change
- A customer evaluating the platform can read this and immediately understand the integration model

This is what framework-first looks like.

---

## 9. The "would this work for project44?" pressure test

For each piece of the framework, I asked: "If project44 were the first vendor instead of FourKites, would the design change?"

| Component | Project44-clean? | Notes |
|---|---|---|
| Internal event types | ✅ | Section 2.4 covers this |
| `IVendorAdapter` interface | ✅ | Generic enough |
| `ClientProfile.ConfigJson` | ✅ | Each adapter parses its own — P44 has OAuth, not apikey; the field accommodates both |
| `VendorDispatcher` | ✅ | Knows nothing about FK |
| `LoadShipperResolver` | ✅ | Queries Vector data; vendor-neutral |
| `OutboundTransaction` row | ✅ | Has `VendorName` column |
| Adapter registry | ✅ | Config-driven, supports any number |
| Rate limit tracker interface | ✅ | Each adapter implements its own; common interface |
| Webhook validator interface | ✅ | Same |

The places where I might have gotten it wrong (worth watching):

- **`LoadStatusType` enum** — if vendor #3 has a status concept that doesn't fit any of these, we add an enum value. Generally additive; low risk.
- **`DocumentType` enum** — same.
- **The 5 events being enough** — fine for OTR API; FBS Phase 2 may need 2-3 more (RateConfirmed, AppointmentRescheduled, etc.). Additive.
- **`ConfigJson` is a stringified blob** — easier than a real schema, but you lose type safety. Tradeoff; can be improved later if it bites.

---

## 10. What I am deliberately NOT building

Saying no to features is more important than saying yes. Things considered and rejected for Phase 1:

| Feature | Why not Phase 1 |
|---|---|
| Per-vendor circuit breaker | Polly handles per-call retries; circuit-breaking is Phase 3 |
| Cross-vendor rate budget coordination | Each vendor's rate limit is independent; coordinating doesn't help anyone |
| Dispatch priority / ordering between vendors | Adapters run in arbitrary order; if order matters, we add it later |
| Synchronous fan-out with vendor responses returned | Callers don't need vendor responses synchronously; results are in the audit log |
| Event versioning / schema evolution | Events are POCOs; adding fields is backward-compatible; we don't need versions yet |
| Persisting events before dispatch (event sourcing) | Audit log captures the request payload — sufficient for replay |
| Webhook-driven event sourcing (FK pushes back, we re-dispatch) | Inbound is a separate flow; designed in Deliverable #5 |
| Dead-letter queue for failed events | Failed transactions stay in `VendorOutboundTransactions` with status; manual retry is fine for now |
| Multi-tenancy (tenant isolation in framework) | Vector is one tenant; if resold to a customer, that's their tenant — same DB, same code |

If any of these become real needs, they're additive. None require redesigning what's here.

---

## 11. SQL schema implications

Two new tables required (full DDL in Deliverable #7):

```sql
-- VendorAPI_FK.dbo.ClientProfiles
CREATE TABLE dbo.ClientProfiles (
    ProfileId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    ShipperCode     NVARCHAR(50) NOT NULL,
    VendorName      NVARCHAR(50) NOT NULL,
    IsActive        BIT NOT NULL DEFAULT 1,
    EnabledEvents   NVARCHAR(500) NOT NULL,   -- CSV
    ConfigJson      NVARCHAR(MAX) NOT NULL,
    CreatedUtc      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedUtc      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_ClientProfiles_ShipperVendor UNIQUE (ShipperCode, VendorName)
);
CREATE INDEX IX_ClientProfiles_Lookup ON dbo.ClientProfiles (ShipperCode, IsActive);

-- VendorAPI_FK.dbo.VendorOutboundTransactions  -- already designed in #2, restated
CREATE TABLE dbo.VendorOutboundTransactions (
    TransactionId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    VendorName           NVARCHAR(50) NOT NULL,
    EventTypeName        NVARCHAR(100) NOT NULL,   -- "LocationReportedEvent" etc.
    VectorLoadId         NVARCHAR(50) NOT NULL,
    SourceSystem         NVARCHAR(50) NULL,        -- "OTR_API", "VectorFBS", "POD_App"
    ShipperCode          NVARCHAR(50) NULL,
    Status               NVARCHAR(20) NOT NULL,    -- PENDING, ACK, CONFIRMED, FAILED, SKIPPED
    HttpStatusCode       INT NULL,
    VendorRequestId      NVARCHAR(100) NULL,
    RequestPayload       NVARCHAR(MAX) NULL,
    ResponseBody         NVARCHAR(MAX) NULL,
    ErrorCategory        NVARCHAR(20) NULL,
    ErrorMessage         NVARCHAR(MAX) NULL,
    DurationMs           INT NULL,
    CreatedUtc           DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedUtc         DATETIME2 NULL
);
CREATE INDEX IX_VOT_VectorLoad ON dbo.VendorOutboundTransactions (VectorLoadId, VendorName, CreatedUtc);
CREATE INDEX IX_VOT_Vendor_Created ON dbo.VendorOutboundTransactions (VendorName, CreatedUtc);
CREATE INDEX IX_VOT_Status ON dbo.VendorOutboundTransactions (Status, CreatedUtc);
```

The `EventTypeName` column is new vs my original Deliverable #2 sketch — it makes the audit log answer the question "what kinds of events failed most often?" easily.

---

## 12. Open items

| ID | Item | Resolution needed before |
|---|---|---|
| O-501 | Where in Vector data does a load's shipper code live? (Specific table/column for LoadShipperResolver) | Production deployment; sandbox can use VECTOR_DEFAULT |
| O-502 | Encryption-at-rest strategy for ConfigJson API keys | Production deployment |
| O-503 | Should adapters be loaded eagerly at app start or lazily on first use? | Performance tuning; default to eager |
| O-504 | Where does the `vendorAdapters` config section live — `Web.config`, separate file, or DB? | Build of `VendorAdapterRegistry` |
| O-505 | Should `ClientProfileRepository` cache results? With what TTL? | Performance; default to 60-second cache |

---

## 13. Done-when checklist

Mark this deliverable complete when:

- [ ] `Vendor.Common.Events` namespace contains all 7 event classes + supporting types
- [ ] `IVendorAdapter`, `VendorOperationResult` defined
- [ ] `ClientProfile`, `ClientProfileRepository` defined
- [ ] `VendorDispatcher`, `VendorAdapterRegistry`, `LoadShipperResolver` defined
- [ ] `Vendor.FourKites.FourKitesAdapter` defined and implements IVendorAdapter
- [ ] FK adapter contains EDI 214 logic (moved from OTR API)
- [ ] Adapter unit-testable: can construct, feed an event, verify output payload
- [ ] Config sections defined in Web.config schema
- [ ] SQL DDL for ClientProfiles and updated VendorOutboundTransactions
- [ ] Pressure test against a second hypothetical vendor documented (Section 9)

---

*End of Vendor.Common Framework Design.*
