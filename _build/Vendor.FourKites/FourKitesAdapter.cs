using System;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;
using Vendor.FourKites.Mapping;
using Vendor.FourKites.RateLimiting;

namespace Vendor.FourKites
{
    /// <summary>
    /// IVendorAdapter implementation for FourKites. Translates vendor-agnostic events
    /// into FK API calls.
    ///
    /// Lifecycle:
    ///   1. Registry instantiates ONCE at app startup
    ///   2. On each dispatch, framework calls CanHandle(evt) then DispatchAsync(evt, profile)
    ///   3. Adapter parses profile.ConfigJson (cheap; per-dispatch), picks endpoint,
    ///      builds payload, checks rate limiter, calls client, returns result
    ///
    /// THREE CONTRACT RULES (from IVendorAdapter docs) — verified in tests:
    ///   1. NEVER throw out of DispatchAsync — every code path returns a VendorOperationResult
    ///   2. SELF-RATE-LIMIT — InMemoryRateLimiter blocks before HTTP if bucket empty
    ///   3. POPULATE AUDIT FIELDS — RequestPayloadJson + ResponseBodyJson set every time
    /// </summary>
    public class FourKitesAdapter : IVendorAdapter, IDisposable
    {
        public string VendorName => "FourKites";

        // Reused across all dispatches. Created in constructor; disposed on disposal.
        private readonly FourKitesClient _client;
        private readonly Action<Exception> _onError;

        // Rate limiter is created lazily per (apiKey, billToCode) combination so a
        // multi-tenant FK deployment doesn't share buckets across tenants. For Phase 1
        // there's just one tenant so this is effectively a single bucket.
        private InMemoryRateLimiter _rateLimiter;
        private string _rateLimiterKey;
        private readonly object _rateLimiterLock = new object();

        /// <summary>
        /// Parameterless constructor — used by VendorAdapterRegistry's reflection.
        /// Default 30-second HTTP timeout, no error handler.
        /// </summary>
        public FourKitesAdapter()
            : this(new FourKitesClient(TimeSpan.FromSeconds(30)), errorHandler: null)
        {
        }

        /// <summary>
        /// Registry-friendly constructor matching the (ClientProfileRepository, Action&lt;Exception&gt;)
        /// shape. ClientProfileRepository is unused — the framework hands us the matching
        /// profile on every dispatch. The error handler is wired into the client.
        /// </summary>
        public FourKitesAdapter(ClientProfileRepository profileRepository, Action<Exception> errorHandler)
            : this(new FourKitesClient(TimeSpan.FromSeconds(30), errorHandler), errorHandler)
        {
        }

        /// <summary>Test-friendly constructor — accepts a pre-built client.</summary>
        public FourKitesAdapter(FourKitesClient client, Action<Exception> errorHandler = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _onError = errorHandler ?? (_ => { });
        }

        // ─── IVendorAdapter ───────────────────────────────────────────────

        /// <summary>
        /// Returns true if FK accepts this event type. FK accepts all framework events
        /// except GenericLoadEvent (FK doesn't know what to do with arbitrary data).
        /// </summary>
        public bool CanHandle(VendorEvent evt)
        {
            if (evt == null) return false;
            return evt is LoadCreatedEvent
                || evt is LoadAssignedEvent
                || evt is LocationReportedEvent
                || evt is LoadStatusEvent
                || evt is LoadTrackingStoppedEvent
                || evt is DocumentAvailableEvent;
        }

        public async Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt,
            ClientProfile profile,
            CancellationToken cancellationToken = default)
        {
            // ─── 1. Defensive null checks ────────────────────────────────
            if (evt == null)
                return VendorOperationResult.Failed("Event was null", "Unknown");
            if (profile == null)
                return VendorOperationResult.Failed("ClientProfile was null", "Unknown");

            // ─── 2. Parse ConfigJson into typed settings ─────────────────
            FourKitesConfig cfg;
            try
            {
                cfg = FourKitesConfig.ParseFrom(profile.ConfigJson);
            }
            catch (FourKitesConfigException ex)
            {
                _onError(ex);
                // Bad config is operator error — permanent until config is fixed.
                return VendorOperationResult.Failed(
                    "Bad ConfigJson for FourKites profile: " + ex.Message, "Permanent");
            }

            // ─── 3. Rate limit check ─────────────────────────────────────
            EnsureRateLimiter(cfg);
            if (!_rateLimiter.TryAcquire())
            {
                return VendorOperationResult.RateLimited(
                    $"Local rate limiter blocked (limit {cfg.RateLimit.RequestsPerSecond}/sec, burst {cfg.RateLimit.BurstSize})");
            }

            // ─── 4. Build payload + URL based on event type ──────────────
            PayloadBuilder.BuildResult payload;
            string endpoint;
            string expectedCallbackType = null;

            try
            {
                switch (evt)
                {
                    case LoadCreatedEvent loadCreated:
                        payload = PayloadBuilder.BuildLoadCreated(loadCreated, cfg);
                        endpoint = cfg.LoadEndpoint;
                        expectedCallbackType = "LOAD_CREATION";  // FK echoes this on the webhook
                        break;

                    case LoadAssignedEvent loadAssigned:
                        payload = PayloadBuilder.BuildLoadAssigned(loadAssigned, cfg);
                        endpoint = cfg.LoadEndpoint;
                        break;

                    case LocationReportedEvent locationReported:
                        payload = PayloadBuilder.BuildLocationReported(locationReported, cfg);
                        endpoint = cfg.LocationEndpoint;
                        break;

                    case LoadStatusEvent loadStatus:
                        payload = PayloadBuilder.BuildLoadStatus(loadStatus, cfg);
                        endpoint = cfg.StatusEndpoint;
                        break;

                    case LoadTrackingStoppedEvent trackingStopped:
                        payload = PayloadBuilder.BuildTrackingStopped(trackingStopped, cfg);
                        endpoint = cfg.StatusEndpoint;
                        break;

                    case DocumentAvailableEvent docAvailable:
                        // For now treat as a JSON metadata POST. A true multipart upload
                        // of the file bytes would require a different client method —
                        // deferring that to when the VB.NET POD path is wired up.
                        payload = PayloadBuilder.BuildDocumentMetadata(docAvailable, cfg);
                        endpoint = cfg.DocumentEndpoint;
                        break;

                    default:
                        return VendorOperationResult.Skipped(
                            $"FK adapter doesn't handle event type {evt.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                return VendorOperationResult.Failed(
                    "Payload build failed: " + ex.Message, "Permanent",
                    requestPayloadJson: null, responseBodyJson: null);
            }

            // ─── 5. POST the payload ─────────────────────────────────────
            var fullUrl = cfg.BaseUrl + endpoint;
            FourKitesResponse response;
            try
            {
                response = await _client.PostJsonAsync(
                    fullUrl, cfg.ApiKey, payload.Json, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Client is contracted not to throw — defensive catch anyway
                _onError(ex);
                return VendorOperationResult.Failed(ex,
                    "Transient");
            }

            // ─── 6. Translate FourKitesResponse → VendorOperationResult ──
            if (response.IsSuccess)
            {
                return VendorOperationResult.Succeeded(
                    httpStatusCode: response.HttpStatusCode ?? 200,
                    vendorRequestId: response.VendorRequestId ?? payload.RequestId,
                    vendorLoadId: response.VendorLoadId,
                    requestPayloadJson: payload.Json,
                    responseBodyJson: response.ResponseBody,
                    expectedCallbackType: expectedCallbackType,
                    duration: response.Duration);
            }

            return VendorOperationResult.Failed(
                response.ErrorMessage ?? "Unknown FK error",
                response.ErrorCategory ?? "Unknown",
                httpStatusCode: response.HttpStatusCode,
                requestPayloadJson: payload.Json,
                responseBodyJson: response.ResponseBody,
                duration: response.Duration);
        }

        // ─── Rate limiter lazy init ───────────────────────────────────────

        /// <summary>
        /// Creates or reuses the rate limiter for the current config's
        /// (apiKey, billToCode) tuple. Lazy because we don't know the config
        /// until the first dispatch, and changing rate-limit settings in
        /// ConfigJson should take effect on the next dispatch.
        /// </summary>
        private void EnsureRateLimiter(FourKitesConfig cfg)
        {
            var key = cfg.ApiKey + "|" + cfg.BillToCode + "|"
                      + cfg.RateLimit.RequestsPerSecond + "|" + cfg.RateLimit.BurstSize;

            if (_rateLimiterKey == key && _rateLimiter != null) return;

            lock (_rateLimiterLock)
            {
                if (_rateLimiterKey == key && _rateLimiter != null) return;

                _rateLimiter = new InMemoryRateLimiter(
                    burstSize: cfg.RateLimit.BurstSize,
                    requestsPerSecond: cfg.RateLimit.RequestsPerSecond);
                _rateLimiterKey = key;
            }
        }

        // ─── IDisposable ──────────────────────────────────────────────────

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
