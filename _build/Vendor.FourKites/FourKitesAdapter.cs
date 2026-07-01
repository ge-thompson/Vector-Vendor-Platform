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
    ///   LoadCreatedEvent          -> POST /api/v1/tracking         (Create, from FBS)
    ///                                Cross-reference captured on 2xx.
    ///   LoadAssignedEvent         -> POST /api/v1/tracking         (Create, if no xref)
    ///                                PATCH /api/v1/tracking/{loadId} (Update, if xref)
    ///                                Distinguishes via LoadCrossReference lookup.
    ///   LocationReportedEvent     -> POST /load/update/dispatcher-api/async (locationUpdate)
    ///   LoadStatusEvent           -> POST /load/update/dispatcher-api/async (eventUpdate)
    ///   LoadTrackingStoppedEvent  -> POST /api/v1/tracking/delete_loads
    ///   DocumentAvailableEvent    -> POST /document-data/upload (base64 JSON)
    ///
    /// Identifier strategy for dispatcher updates: the dispatcher endpoint identifies
    /// loads by loadNumber + billToCode (not the FK-issued loadId), so location/status
    /// dispatch does NOT require a cross-reference lookup. Cross-ref is only needed for
    /// Update (PATCH) and Delete operations that target the FK loadId in the URL path.
    ///
    /// THREE CONTRACT RULES (from IVendorAdapter docs) — verified in tests:
    ///   1. NEVER throw out of DispatchAsync — every code path returns a VendorOperationResult
    ///   2. SELF-RATE-LIMIT — InMemoryRateLimiter blocks before HTTP if bucket empty
    ///   3. POPULATE AUDIT FIELDS — RequestPayloadJson + ResponseBodyJson set every time
    /// </summary>
    public class FourKitesAdapter : IVendorAdapter, IVendorLoadReader, IDisposable
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
        /// Returns true if FK accepts this event type. Every supported event type maps to
        /// a real FK call (no Skipped events in the current routing).
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
            // (No quick-exits currently — every event type below routes to a real FK call.)

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
                    case LoadCreatedEvent loadCreated:
                        // Create can originate from FBS (LoadCreatedEvent) OR from OTR API
                        // (LoadAssignedEvent). Route both through the same Create-or-Update logic
                        // keyed on the cross-reference table.
                        return await DispatchLoadCreatedAsync(loadCreated, cfg, cancellationToken).ConfigureAwait(false);

                    case LoadAssignedEvent loadAssigned:
                        return await DispatchLoadAssignedAsync(loadAssigned, cfg, cancellationToken).ConfigureAwait(false);

                    case LocationReportedEvent location:
                        return await DispatchLocationAsync(location, cfg, cancellationToken).ConfigureAwait(false);

                    case LoadStatusEvent status:
                        return await DispatchStatusAsync(status, cfg, cancellationToken).ConfigureAwait(false);

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

        // ─── IVendorLoadReader — diagnostic GET path ───────────────────

        /// <summary>
        /// Fetches FK's current view of a load by VectorLoadId. Used by the diagnostic
        /// /api/vendordispatch/load/{customerId}/{vectorLoadId} endpoint to confirm a load
        /// exists in FK and to inspect its stops (identifiers, appointment times, etc.).
        ///
        /// Routes to GET /shipments/{loadNumber}?identifierType=loadNumber so callers don't
        /// need to know FK's internal loadId. Shares the same apikey rate-limit bucket as
        /// the write endpoints — the in-process rate limiter applies here too.
        ///
        /// NEVER THROWS. All failures (config, rate limit, HTTP, transient errors) come
        /// back in the VendorLoadReadResult.
        /// </summary>
        public async Task<VendorLoadReadResult> GetLoadAsync(
            string vectorLoadId,
            ClientProfile profile,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(vectorLoadId))
                return VendorLoadReadResult.Failed("vectorLoadId was null or empty", "Permanent");
            if (profile == null)
                return VendorLoadReadResult.Failed("ClientProfile was null", "Permanent");

            FourKitesConfig cfg;
            try
            {
                cfg = FourKitesConfig.ParseFrom(profile.ConfigJson);
            }
            catch (FourKitesConfigException ex)
            {
                _onError(ex);
                return VendorLoadReadResult.Failed(
                    "Bad ConfigJson for FourKites profile: " + ex.Message, "Permanent");
            }

            // Apply the same rate limiter as writes — FK GET shares the apikey bucket.
            EnsureRateLimiter(cfg);
            if (!_rateLimiter.TryAcquire())
            {
                return VendorLoadReadResult.Failed(
                    $"Local rate limiter blocked GET (limit {cfg.RateLimit.RequestsPerSecond}/sec, burst {cfg.RateLimit.BurstSize})",
                    "RateLimit");
            }

            var url = cfg.BaseUrl + cfg.ShipmentDetailsEndpoint(vectorLoadId);

            try
            {
                var resp = await _client.GetJsonAsync(url, cfg.ApiKey, cancellationToken).ConfigureAwait(false);

                if (resp.IsSuccess)
                    return VendorLoadReadResult.Ok(resp.HttpStatusCode ?? 200, resp.ResponseBody, resp.Duration);

                return VendorLoadReadResult.Failed(
                    resp.ErrorMessage ?? "Unknown FK error",
                    resp.ErrorCategory ?? "Unknown",
                    resp.HttpStatusCode,
                    resp.ResponseBody,
                    resp.Duration);
            }
            catch (Exception ex)
            {
                _onError(ex);
                return VendorLoadReadResult.Failed(
                    "FK adapter unhandled GET error: " + ex.Message, "Permanent");
            }
        }

        // ─── LoadCreatedEvent: Create (from FBS, no driver yet) ────────

        private async Task<VendorOperationResult> DispatchLoadCreatedAsync(
            LoadCreatedEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            // FBS fires LoadCreatedEvent when the load first lands from the customer (EDI 204).
            // At this point we know the stops and load metadata but no driver is assigned yet —
            // trackingInfo (driverPhone, truck #, trailer #) will land later via LoadAssignedEvent
            // when OTR API TrackLoad fires.

            // Check for existing cross-ref. If the load already exists in FK (unusual, but
            // could happen if FBS replays an event), skip to avoid creating a duplicate.
            if (_crossRef != null)
            {
                try
                {
                    var existing = await _crossRef.GetFkLoadIdAsync(evt.VectorLoadId, ct).ConfigureAwait(false);
                    if (existing.HasValue)
                    {
                        return VendorOperationResult.Skipped(
                            "FK load already exists for VectorLoadId " + evt.VectorLoadId +
                            " (FK loadId " + existing.Value + "). Skipping duplicate Create.");
                    }
                }
                catch (Exception lookupEx)
                {
                    _onError(lookupEx);
                    // Fall through to Create — FK will reject duplicates if it has the load.
                }
            }

            var payload = PayloadBuilder.BuildLoadCreateFromCreated(evt, cfg);
            var createUrl = cfg.BaseUrl + cfg.LoadCreateEndpoint;
            var response = await _client.PostJsonAsync(createUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);

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
                        _onError(persistEx);
                    }
                }
            }

            return TranslateResponse(response, payload, expectedCallbackType: "LOAD_CREATION");
        }

        // ─── LoadAssignedEvent: Create-or-Update routing ──────────────

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

        // ─── LocationReportedEvent: dispatcher update with locationUpdate ─────

        private async Task<VendorOperationResult> DispatchLocationAsync(
            LocationReportedEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            // Dispatcher endpoint identifies the load by loadNumber + billToCode — no FK loadId
            // needed, so no cross-reference lookup required. This is the high-volume path
            // (one event every ~15 minutes per active load).
            var payload = PayloadBuilder.BuildDispatcherLocation(evt, cfg);
            var fullUrl = cfg.BaseUrl + cfg.DispatcherUpdateEndpoint;
            var response = await _client.PostJsonAsync(fullUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);
            return TranslateResponse(response, payload);
        }

        // ─── LoadStatusEvent: dispatcher update with eventUpdate ──────────

        private async Task<VendorOperationResult> DispatchStatusAsync(
            LoadStatusEvent evt, FourKitesConfig cfg, CancellationToken ct)
        {
            // Same endpoint as location; eventUpdate sub-object carries the status code.
            var payload = PayloadBuilder.BuildDispatcherStatus(evt, cfg);
            var fullUrl = cfg.BaseUrl + cfg.DispatcherUpdateEndpoint;
            var response = await _client.PostJsonAsync(fullUrl, cfg.ApiKey, payload.Json, ct).ConfigureAwait(false);
            return TranslateResponse(response, payload);
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
