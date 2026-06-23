using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;
using Vendor.Common.Persistence;

namespace Vendor.Common.Dispatch
{
    /// <summary>
    /// THE single API entry point callers use to dispatch vendor-agnostic events.
    /// Used by OTR API, VB.NET POD app, and (Phase 2) FBS.
    ///
    /// USAGE:
    ///   1. At app startup (Application_Start / Main / Form_Load), call Configure(...).
    ///      Throws loudly if config is bad.
    ///   2. Anywhere in business code, call:
    ///         VendorDispatcher.Instance.Dispatch(new LocationReportedEvent { ... });
    ///      Returns immediately. Audit + actual vendor call happen on a background Task.
    ///
    /// CONTRACT:
    ///   - Dispatch() NEVER throws. Every error is caught and audited as DEAD_LETTER.
    ///   - Caller's thread is never blocked by I/O.
    ///   - The dispatcher logs to VendorOutboundTransactions for every code path
    ///     (success, failure, skipped, no-matching-profile).
    ///
    /// FAN-OUT: if multiple profiles match (shipper X has two vendors
    /// configured for the same event), the dispatcher invokes BOTH adapters in parallel.
    /// Phase 1 has one vendor; the structure is ready for vendor #2 with zero changes.
    /// </summary>
    public class VendorDispatcher
    {
        // ─── Singleton plumbing ──────────────────────────────────────────────

        private static VendorDispatcher _instance;
        private static readonly object _initLock = new object();

        /// <summary>
        /// The configured singleton. Throws if Configure has not been called.
        /// </summary>
        public static VendorDispatcher Instance
        {
            get
            {
                var current = _instance;
                if (current == null)
                    throw new InvalidOperationException(
                        "VendorDispatcher has not been initialized. Call VendorDispatcher.Configure(...) " +
                        "at application startup (Application_Start / Main / Form_Load).");
                return current;
            }
        }

        /// <summary>True if the singleton has been configured. Callers can use this to
        /// guard against early calls during startup without throwing.</summary>
        public static bool IsConfigured => _instance != null;

        /// <summary>
        /// The vendor adapter registry built at Configure() time. Exposed so the inbound
        /// webhook controller can resolve per-vendor validators and processors without
        /// rebuilding the registry or re-reading config. Throws if not yet configured.
        /// </summary>
        public VendorAdapterRegistry Registry => _registry;

        /// <summary>
        /// The audit/vendor connection string supplied at Configure() time. Exposed so
        /// inbound webhook persistence can reuse the same connection without a second
        /// config lookup.
        /// </summary>
        public string AuditConnectionString => _auditConnectionString;

        /// <summary>
        /// Initializes the singleton from configuration. Idempotent — safe to call
        /// multiple times (subsequent calls are no-ops). Loads from these app settings:
        ///
        ///   VendorDispatch.Enabled              (bool; default true)
        ///   VendorDispatch.FireAndForget        (bool; default true)
        ///   VendorDispatch.AuditConnectionString (required)
        ///
        /// And the &lt;vendorAdapters&gt; config section.
        /// </summary>
        public static void Configure(Action<Exception> errorHandler = null)
        {
            if (_instance != null) return;

            lock (_initLock)
            {
                if (_instance != null) return;

                var connectionString = ConfigurationManager.AppSettings["VendorDispatch.AuditConnectionString"];
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "AppSetting 'VendorDispatch.AuditConnectionString' is required. " +
                        "Set it in Web.config / App.config to your VendorAPI_FK connection string.");

                // Initialize the status-mapping cache early. Errors during load surface via
                // errorHandler but never block startup — mapper code falls back to hardcoded
                // templates if the cache is empty.
                VendorStatusMappingStore.Initialize(connectionString, errorHandler);

                var enabled = ParseBoolAppSetting("VendorDispatch.Enabled", defaultValue: true);
                var fireAndForget = ParseBoolAppSetting("VendorDispatch.FireAndForget", defaultValue: true);

                var section = VendorAdaptersSection.Load();
                if (section == null)
                    throw new InvalidOperationException(
                        "Configuration section <vendorAdapters> is missing. Declare it in " +
                        "<configSections> and add adapter rows to Web.config / App.config.");

                var profileRepo = new ClientProfileRepository(connectionString, errorHandler: errorHandler);
                var outboundRepo = new OutboundTransactionRepository(connectionString, errorHandler);
                var registry = new VendorAdapterRegistry(section, profileRepo, errorHandler);
                var resolver = new LoadShipperResolver(connectionString);

                _instance = new VendorDispatcher(
                    enabled, fireAndForget, registry, profileRepo, outboundRepo, resolver, errorHandler, connectionString);
            }
        }

        /// <summary>
        /// Test-friendly initialization that bypasses ConfigurationManager. Tests build
        /// the collaborators directly and inject them. Production code uses Configure().
        /// </summary>
        public static void ConfigureForTesting(
            bool enabled,
            bool fireAndForget,
            VendorAdapterRegistry registry,
            ClientProfileRepository profileRepository,
            OutboundTransactionRepository auditRepository,
            LoadShipperResolver shipperResolver,
            Action<Exception> errorHandler = null)
        {
            lock (_initLock)
            {
                _instance = new VendorDispatcher(
                    enabled, fireAndForget,
                    registry, profileRepository, auditRepository, shipperResolver,
                    errorHandler);
            }
        }

        /// <summary>Resets the singleton (test support). Do NOT call in production.</summary>
        public static void ResetForTesting()
        {
            lock (_initLock) { _instance = null; }
        }

        /// <summary>
        /// Refreshes the status-mapping cache from the database. Call this after
        /// editing rows in dbo.VendorStatusMapping to apply changes without restarting
        /// the host application. Safe to call concurrently with active dispatches —
        /// the cache swap is atomic.
        /// </summary>
        public void RefreshStatusMappings()
        {
            if (VendorStatusMappingStore.IsInitialized)
                VendorStatusMappingStore.Instance.Refresh();
        }

        /// <summary>
        /// Reads the dispatch verbosity ("Generous" or "Conservative") from the
        /// ClientProfile.ConfigJson dispatchPolicy.verbosity field. Defaults to
        /// "Generous" if the field is missing, the profile doesn't exist, or any
        /// error occurs. Never throws — callers can use the return value directly.
        ///
        /// Used by event producers like OTR API's SendStatus to decide whether to
        /// emit one event per data point (Generous) or just the freshest (Conservative).
        /// The adapter's rate limiter is the second line of defense against flooding
        /// vendors regardless of verbosity choice.
        /// </summary>
        public string GetDispatchVerbosity(string vendorName, string shipperCode = "VECTOR_DEFAULT")
        {
            const string DefaultVerbosity = "Generous";
            if (string.IsNullOrEmpty(vendorName)) return DefaultVerbosity;

            try
            {
                var all = _profileRepository.GetAllProfiles();
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (!p.IsActive) continue;
                    if (!string.Equals(p.VendorName, vendorName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(p.ShipperCode, shipperCode, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrWhiteSpace(p.ConfigJson)) return DefaultVerbosity;

                    var jo = JObject.Parse(p.ConfigJson);
                    var token = jo.SelectToken("dispatchPolicy.verbosity");
                    var verbosity = token?.ToString();
                    return string.IsNullOrWhiteSpace(verbosity) ? DefaultVerbosity : verbosity;
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
            return DefaultVerbosity;
        }

        // ─── Instance state ──────────────────────────────────────────────────

        private readonly bool _enabled;
        private readonly bool _fireAndForget;
        private readonly VendorAdapterRegistry _registry;
        private readonly ClientProfileRepository _profileRepository;
        private readonly OutboundTransactionRepository _auditRepository;
        private readonly LoadShipperResolver _shipperResolver;
        private readonly Action<Exception> _onError;
        private readonly string _auditConnectionString;

        private VendorDispatcher(
            bool enabled,
            bool fireAndForget,
            VendorAdapterRegistry registry,
            ClientProfileRepository profileRepository,
            OutboundTransactionRepository auditRepository,
            LoadShipperResolver shipperResolver,
            Action<Exception> errorHandler,
            string auditConnectionString = null)
        {
            _enabled = enabled;
            _fireAndForget = fireAndForget;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
            _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
            _shipperResolver = shipperResolver ?? throw new ArgumentNullException(nameof(shipperResolver));
            _onError = errorHandler ?? (_ => { });
            _auditConnectionString = auditConnectionString;
        }

        // ─── The public dispatch surface ─────────────────────────────────────

        /// <summary>
        /// Fire-and-forget dispatch. Returns immediately. Caller's thread is never
        /// blocked by I/O. NEVER throws.
        /// </summary>
        public void Dispatch(VendorEvent evt)
        {
            if (evt == null) return;
            if (!_enabled) return;

            if (_fireAndForget)
            {
                // Task.Run hands off to the thread pool; we don't await it.
                // Inside DispatchInternalAsync, all errors are caught and audited.
                _ = Task.Run(() => DispatchInternalAsync(evt, CancellationToken.None));
            }
            else
            {
                // FireAndForget = false: run synchronously. Used in tests where we
                // want to assert state immediately after Dispatch returns.
                try
                {
                    DispatchInternalAsync(evt, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Defensive — should never reach here, DispatchInternalAsync catches.
                    _onError(ex);
                }
            }
        }

        /// <summary>
        /// Async dispatch that callers can await. Same semantics as Dispatch except
        /// the caller knows when the work completes. NEVER throws.
        /// </summary>
        public async Task DispatchAsync(VendorEvent evt, CancellationToken cancellationToken = default)
        {
            if (evt == null) return;
            if (!_enabled) return;
            await DispatchInternalAsync(evt, cancellationToken).ConfigureAwait(false);
        }

        // ─── Internals ───────────────────────────────────────────────────────

        private async Task DispatchInternalAsync(VendorEvent evt, CancellationToken ct)
        {
            try
            {
                // 1. Resolve shipper from the load id (Phase 1 always returns VECTOR_DEFAULT)
                var shipperCode = _shipperResolver.Resolve(evt.VectorLoadId);

                // 2. Find matching profiles via the cache
                var eventTypeName = evt.GetType().Name;
                var matches = _profileRepository.FindRouting(shipperCode, eventTypeName);

                if (matches.Count == 0)
                {
                    // No vendor wants this event. Audit as SKIPPED for visibility.
                    await _auditRepository.InsertSkippedAsync(evt,
                        vendorName: "(none)",
                        reason: $"No active profile matches ShipperCode='{shipperCode}', EventType='{eventTypeName}'.",
                        ct: ct).ConfigureAwait(false);
                    return;
                }

                // 3. For each matched profile, look up adapter and dispatch in parallel
                var tasks = new Task[matches.Count];
                for (int i = 0; i < matches.Count; i++)
                {
                    var profile = matches[i];
                    tasks[i] = DispatchToOneVendorAsync(evt, profile, shipperCode, ct);
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Catch-all safety net: dispatcher-level failure (shipper lookup threw,
                // registry threw, etc.). Audit + report; do not propagate.
                _onError(ex);
                try
                {
                    await _auditRepository.InsertDispatcherErrorAsync(evt, ex, ct).ConfigureAwait(false);
                }
                catch
                {
                    // If even the audit write fails, we've done everything we can.
                }
            }
        }

        private async Task DispatchToOneVendorAsync(
            VendorEvent evt, ClientProfile profile, string shipperCode, CancellationToken ct)
        {
            var vendorName = profile.VendorName;
            long txId = 0;

            try
            {
                var adapter = _registry.GetAdapter(vendorName);
                if (adapter == null)
                {
                    // Profile points at a vendor we don't have an adapter for.
                    // Likely misconfiguration; audit as SKIPPED so it shows on the dashboard.
                    await _auditRepository.InsertSkippedAsync(evt, vendorName,
                        $"No adapter registered for vendor '{vendorName}'. Check <vendorAdapters> config.",
                        ct).ConfigureAwait(false);
                    return;
                }

                if (!adapter.CanHandle(evt))
                {
                    await _auditRepository.InsertSkippedAsync(evt, vendorName,
                        $"Adapter '{vendorName}' declined event type '{evt.GetType().Name}' via CanHandle.",
                        ct).ConfigureAwait(false);
                    return;
                }

                // Insert PENDING row before calling adapter
                txId = await _auditRepository.InsertPendingAsync(evt, vendorName, shipperCode, ct)
                                             .ConfigureAwait(false);

                // Call the adapter. Adapter MUST NOT throw per the contract — but we
                // wrap defensively anyway.
                var sw = Stopwatch.StartNew();
                VendorOperationResult result;
                try
                {
                    result = await adapter.DispatchAsync(evt, profile, ct).ConfigureAwait(false);
                    if (result == null)
                    {
                        // Adapter violated contract by returning null. Treat as failure.
                        result = VendorOperationResult.Failed(
                            "Adapter returned null VendorOperationResult.", "Unknown");
                    }
                }
                catch (Exception adapterEx)
                {
                    // Adapter violated the "never throw" rule. Catch defensively.
                    _onError(adapterEx);
                    result = VendorOperationResult.Failed(adapterEx, "Unknown");
                }

                sw.Stop();
                if (result.Duration == TimeSpan.Zero)
                    result.Duration = sw.Elapsed;

                // Record outcome
                await _auditRepository.RecordOutcomeAsync(txId, result, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Per-vendor failure safety net. Other vendors in fan-out are unaffected.
                _onError(ex);
                if (txId > 0)
                {
                    var failResult = VendorOperationResult.Failed(ex, "Unknown");
                    try
                    {
                        await _auditRepository.RecordOutcomeAsync(txId, failResult, ct).ConfigureAwait(false);
                    }
                    catch { /* audit best-effort */ }
                }
            }
        }

        private static bool ParseBoolAppSetting(string key, bool defaultValue)
        {
            var raw = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            return bool.TryParse(raw, out var result) ? result : defaultValue;
        }
    }
}
