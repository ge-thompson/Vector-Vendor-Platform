using System;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;
using Vendor.FourKites.Mapping;
using Vendor.FourKites.Persistence;
using Vendor.FourKites.RateLimiting;

namespace Vendor.FourKites
{
    /// <summary>
    /// IVendorAdapter implementation for FourKites. Translates vendor-agnostic events
    /// into FK API calls per the official spec at docs.fourkites.com/api-reference.
    ///
    /// Event routing:
    ///   LoadAssignedEvent         -> POST /api/v1/tracking         (first time)
    ///                                PATCH /api/v1/tracking/{loadId} (subsequent)
    ///                                Distinguishes via LoadCrossReference lookup
    ///   LoadTrackingStoppedEvent  -> POST /api/v1/tracking/delete_loads
    ///   DocumentAvailableEvent    -> POST /document-data/upload (base64 JSON)
    ///
    ///   LoadCreatedEvent          -> SKIPPED  (Phase 2 will originate Create from FBS;
    ///                                          Phase 1 origin is LoadAssignedEvent above)
    ///   LoadStatusEvent           -> SKIPPED  (O-006: FK does its own tracking once
    ///                                          driverPhone is handed off; no FK endpoint
    ///                                          for pushing TT-sourced status updates IN)
    ///   LocationReportedEvent     -> SKIPPED  (same reason as LoadStatusEvent)
    ///
    /// THREE CONTRACT RULES (from IVendorAdapter docs) — verified in tests:
    ///   1. NEVER throw out of DispatchAsync — every code path returns a VendorOperationResult
    ///   2. SELF-RATE-LIMIT — InMemoryRateLimiter blocks before HTTP if bucket empty
    ///   3. POPULATE AUDIT FIELDS — RequestPayloadJson + ResponseBodyJson set every time
    /// </summary>
    public class FourKitesAdapter : IVendorAdapter, IDisposable
    {
        public string VendorName => "FourKites";

        private readonly FourKitesClient _client;
        private readonly Action<Exception> _onError;
        private readonly LoadCrossReferenceStore _crossRef;

        // Rate limiter is per-(apiKey, billToCode) so multi-tenant deployments don't share buckets.
        private InMemoryRateLimiter _rateLimiter;
        private string _rateLimiterKey;
        private readonly object _rateLimiterLock = new object();

        /// <summary>
        /// Parameterless constructor — used by VendorAdapterRegistry's reflection.
        /// Loads the audit DB connection string from VendorDispatch.AuditConnectionString
        /// (set in Web.config). Without that key the cross-reference lookup will fail —
        /// the registry's normal constructor below is preferred.
        /// </summary>
        public FourKitesAdapter()
            : this(new FourKitesClient(TimeSpan.FromSeconds(30)),
                   crossRef: BuildCrossRefFromAppSettings(),
                   errorHandler: null)
        {
        }

        /// <summary>
        /// Registry-friendly constructor matching the (ClientProfileRepository, Action&lt;Exception&gt;)
        /// shape. Cross-reference store is built from VendorDispatch.AuditConnectionString.
        /// </summary>
        public FourKitesAdapter(ClientProfileRepository profileRepository, Action<Exception> errorHandler)
            : this(new FourKitesClient(TimeSpan.FromSeconds(30), errorHandler),
                   crossRef: BuildCrossRefFromAppSettings(),
                   errorHandler: errorHandler)
        {
        }

        /// <summary>Test-friendly constructor — accepts a pre-built client and store.</summary>
        public FourKitesAdapter(
            FourKitesClient client,
            LoadCrossReferenceStore crossRef,
            Action<Exception> errorHandler = null)
        {
            _client   = client   ?? throw new ArgumentNullException(nameof(client));
            _crossRef = crossRef; // nullable — adapter will degrade to always-Create if absent
            _onError  = errorHandler ?? (_ => { });
        }

        // ─── IVendorAdapter ───────────────────────────────────────────────

        /// <summary>
        /// Returns true if FK accepts this event type. The adapter dispatches:
        ///   LoadAssignedEvent, LoadTrackingStoppedEvent, DocumentAvailableEvent.
        /// LoadCreatedEvent / LoadStatusEvent / LocationReportedEvent are accepted
        /// at this layer but dispatched as Skipped (see DispatchAsync).
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
            CancellationToken cancellationToken = default(CancellationToken))
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
                return VendorOperationResult.Failed(
                    "Bad ConfigJson for FourKites profile: " + ex.Message, "Permanent");
            }

            // ─── 3. Quick exits for events FK doesn't accept ─────────────
            // See class-level routing comment for the rationale on each.

            if (evt is LoadCreatedEvent)
                return VendorOperationResult.Skipped(
                    "FK Phase 1 originates Create via LoadAssignedEvent. LoadCreatedEvent is Phase 2 (FBS origin).");

            if (evt is LoadStatusEvent || evt is LocationReportedEvent)
                return VendorOperationResult.Skipped(
                    "FK does its own truck tracking once driverPhone is handed off (O-006). " +
                    "No FK endpoint exists for pushing TT-sourced status/location updates IN.");

            // ─── 4. Rate limit check (only for events that actually hit FK) ─
            EnsureRateLimiter(cfg);
            if (!_rateLimiter.TryAcquire())
            {
                return VendorOperationResult.RateLimited(
                    $"Local rate limiter blocked (limit {cfg.RateLimit.RequestsPerSecond}/sec, burst {cfg.RateLimit.BurstSize})");
            }

            // ─── 5. Route + execute ─────────────────────────────────────
            try
            {
                switch (evt)
                {
                    case LoadAssignedEvent loadAssigned:
                        return await DispatchLoadAssignedAsync(loadAssigned, cfg, cancellationToken).ConfigureAwait(false);

                    case LoadTrackingStoppedEvent stopped:
                        return await DispatchLoadStoppedAsync(stopped, cfg, cancellationToken).ConfigureAwait(false);

                    case DocumentAvailableEvent docAvailable:
                        return await DispatchDocumentAsync(docAvailable, cfg, cancellationToken).ConfigureAwait(false);

                    default:
                        return VendorOperationResult.Skipped(
                            $"FK adapter doesn't handle event type {evt.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                return VendorOperationResult.Failed(
                    "FK adapter unhandled error: " + ex.Message, "Permanent");
            }
        }

        // ─── LoadAssignedEvent: Create-or-Update routing ──────────────────

        private async Task<VendorOperationResult> DispatchLoadAssignedAsync(
            LoadAssignedEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            // Lookup existing FK loadId for this VectorLoadId.
            long? existingFkLoadId = null;
            if (_crossRef != null)
            {
                try
                {
                    existingFkLoadId = await _crossRef.GetFkLoadIdAsync(evt.VectorLoadId, ct).ConfigureAwait(false);
                }
                catch (Exception lookupEx)
                {
                    // Cross-ref lookup failure is non-fatal — fall back to Create.
                    // Worst case: FK rejects with "load already exists" and we audit it.
                    _onError(lookupEx);
                }
            }

            PayloadBuilder.BuildResult payload;
            FourKitesResponse response;

            if (existingFkLoadId.HasValue)
            {
                // ─── Update path (PATCH) ───
                payload = PayloadBuilder.BuildLoadUpdate(evt, cfg);
                var fullUrl = cfg.BaseUrl + cfg.LoadUpdateEndpoint(existingFkLoadId.Value);
                response = await _client.PatchJsonAsync(fullUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);

                return TranslateResponse(response, payload, expectedCallbackType: "LOAD_UPDATION");
            }

            // ─── Create path (POST) ───
            payload = PayloadBuilder.BuildLoadCreate(evt, cfg);
            var createUrl = cfg.BaseUrl + cfg.LoadCreateEndpoint;
            response = await _client.PostJsonAsync(createUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);

            // On 2xx, capture FK loadId for future Update/Delete lookups.
            if (response.IsSuccess && _crossRef != null)
            {
                if (long.TryParse(response.VendorLoadId, out var newFkLoadId) && newFkLoadId > 0)
                {
                    try
                    {
                        await _crossRef.PersistAsync(evt.VectorLoadId, newFkLoadId, "ACTIVE", ct).ConfigureAwait(false);
                    }
                    catch (Exception persistEx)
                    {
                        // Persist failure is non-fatal but bad — future Updates will re-Create
                        // and produce duplicates in FK. Log loudly.
                        _onError(persistEx);
                    }
                }
            }

            return TranslateResponse(response, payload, expectedCallbackType: "LOAD_CREATION");
        }

        // ─── LoadTrackingStoppedEvent: Delete ────────────────────────────

        private async Task<VendorOperationResult> DispatchLoadStoppedAsync(
            LoadTrackingStoppedEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            // Need FK loadId to call delete. Lookup cross-ref.
            long? fkLoadId = null;
            if (_crossRef != null)
            {
                try
                {
                    fkLoadId = await _crossRef.GetFkLoadIdAsync(evt.VectorLoadId, ct).ConfigureAwait(false);
                }
                catch (Exception lookupEx)
                {
                    _onError(lookupEx);
                }
            }

            if (!fkLoadId.HasValue)
            {
                // No FK loadId on record. This usually means LoadAssignedEvent never succeeded
                // (HTTP_FAIL on Create, never got an FK loadId), or the load was never tracked
                // by FK in the first place. Either way, nothing to delete.
                return VendorOperationResult.Skipped(
                    "No FK loadId in cross-reference for VectorLoadId " + evt.VectorLoadId +
                    " — nothing to delete. Reason: " + (evt.Reason ?? "unknown"));
            }

            var payload = PayloadBuilder.BuildLoadDelete(fkLoadId.Value);
            var fullUrl = cfg.BaseUrl + cfg.LoadDeleteEndpoint;
            var response = await _client.PostJsonAsync(fullUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);

            // On 2xx, mark cross-ref STOPPED so future Updates know not to PATCH a dead load.
            if (response.IsSuccess && _crossRef != null)
            {
                try { await _crossRef.MarkStoppedAsync(evt.VectorLoadId, ct).ConfigureAwait(false); }
                catch (Exception ex) { _onError(ex); }
            }

            return TranslateResponse(response, payload);
        }

        // ─── DocumentAvailableEvent: Upload ──────────────────────────────

        private async Task<VendorOperationResult> DispatchDocumentAsync(
            DocumentAvailableEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            var payload = PayloadBuilder.BuildDocumentUpload(evt, cfg);
            var fullUrl = cfg.BaseUrl + cfg.DocumentUploadEndpoint;
            var response = await _client.PostJsonAsync(fullUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);
            return TranslateResponse(response, payload);
        }

        // ─── Translation helpers ──────────────────────────────────────────

        private static VendorOperationResult TranslateResponse(
            FourKitesResponse response,
            PayloadBuilder.BuildResult payload,
            string expectedCallbackType = null)
        {
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

        // ─── Cross-ref construction ───────────────────────────────────────

        /// <summary>
        /// Build a LoadCrossReferenceStore from the framework's audit connection string
        /// (set as VendorDispatch.AuditConnectionString in Web.config). Returns null if
        /// not configured — adapter degrades to always-Create with a soft warning.
        /// </summary>
        private static LoadCrossReferenceStore BuildCrossRefFromAppSettings()
        {
            try
            {
                var cs = System.Configuration.ConfigurationManager.AppSettings["VendorDispatch.AuditConnectionString"];
                if (string.IsNullOrWhiteSpace(cs)) return null;
                return new LoadCrossReferenceStore(cs);
            }
            catch
            {
                return null;
            }
        }

        // ─── IDisposable ──────────────────────────────────────────────────

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
