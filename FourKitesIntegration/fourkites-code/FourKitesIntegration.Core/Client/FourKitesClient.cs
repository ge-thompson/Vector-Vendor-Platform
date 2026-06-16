using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FourKitesIntegration.Core.Models.CreateShipment;
using FourKitesIntegration.Core.Models.DispatcherUpdate;
using FourKitesIntegration.Core.Models.Documents;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;

namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Thread-safe client for the FourKites API. Construct ONCE per process and reuse.
    /// Wraps Create Shipment, Dispatcher Update (Async), Upload Document, and Get Document.
    /// </summary>
    public sealed class FourKitesClient : IDisposable
    {
        private static readonly MediaTypeHeaderValue JsonContentType =
            new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        private static readonly Uri DummyBase = new Uri("https://placeholder.invalid/");

        private readonly FourKitesClientOptions _options;
        private readonly HttpClient _http;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
        private bool _disposed;

        public RateLimitTracker RateLimitTracker { get; } = new RateLimitTracker();

        public FourKitesClient(FourKitesClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(_options.ApiKey))
                throw new ArgumentException("ApiKey is required.", nameof(options));
            if (string.IsNullOrEmpty(_options.BaseHost))
                throw new ArgumentException("BaseHost is required.", nameof(options));

            _http = new HttpClient { Timeout = _options.HttpTimeout };

            _retryPolicy = Policy
                .HandleResult<HttpResponseMessage>(r =>
                {
                    int s = (int)r.StatusCode;
                    return s == 429 || (s >= 500 && s < 600);
                })
                .Or<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    _options.MaxRetryAttempts,
                    attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))); // 2s, 4s, 8s
        }

        // ─── Create Shipment ─────────────────────────────────────────────────

        /// <summary>
        /// POST /api/v1/tracking — creates a new load in FourKites.
        /// Idempotency: NOT automatically idempotent. Capture FourKitesLoadId from the LOAD_CREATION webhook
        /// before any retry to avoid duplicate loads. See Reference doc Section 3.4.
        /// </summary>
        public Task<FourKitesResponse> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Post, "/api/v1/tracking", request, ct);

        // ─── Dispatcher Update (Async) ───────────────────────────────────────

        /// <summary>
        /// POST /load/update/dispatcher-api/async — sends a batch of updates for one or more loads.
        /// Returns 202 Accepted on success; business validation happens asynchronously after acceptance.
        /// Idempotent: safe to retry without duplicating loads.
        /// </summary>
        public Task<FourKitesResponse> SendDispatcherUpdateAsync(DispatcherBatch batch, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Post, "/load/update/dispatcher-api/async", batch, ct);

        // ─── Documents ───────────────────────────────────────────────────────

        /// <summary>
        /// POST /document-data/upload — uploads a base64-encoded document (PDF, JPEG, or TIFF) for a load.
        /// Max 10 MB per call. Async — the doc may take a moment to appear in the FourKites UI.
        /// </summary>
        public Task<FourKitesResponse> UploadDocumentAsync(UploadDocumentRequest request, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Post, "/document-data/upload", request, ct);

        /// <summary>
        /// GET /document-data — retrieves a previously uploaded document for verification.
        /// Returns the base64 file content if found, or 404 if not.
        /// </summary>
        public async Task<DocumentDownloadResult> GetDocumentAsync(
            string loadIdentifier,
            string loadValue,
            string documentType,
            string stopIdentifier = null,
            string stopValue = null,
            CancellationToken ct = default)
        {
            var query = $"?load_identifier={Uri.EscapeDataString(loadIdentifier)}" +
                        $"&load_value={Uri.EscapeDataString(loadValue)}" +
                        $"&document_type={Uri.EscapeDataString(documentType)}";
            if (!string.IsNullOrEmpty(stopIdentifier) && !string.IsNullOrEmpty(stopValue))
            {
                query += $"&stop_identifier={Uri.EscapeDataString(stopIdentifier)}";
                query += $"&stop_value={Uri.EscapeDataString(stopValue)}";
            }

            var response = await SendAsync<object>(HttpMethod.Get, "/document-data" + query, null, ct).ConfigureAwait(false);
            if (!response.IsSuccess) return null;
            return FourKitesJson.Deserialize<DocumentDownloadResult>(response.Body);
        }

        // ─── Core HTTP plumbing ──────────────────────────────────────────────

        private Task<FourKitesResponse> SendAsync(HttpMethod method, string path, object payload, CancellationToken ct) =>
            SendAsync<object>(method, path, payload, ct);

        private async Task<FourKitesResponse> SendAsync<T>(HttpMethod method, string path, object payload, CancellationToken ct)
        {
            await RateLimitTracker.ThrottleIfNeededAsync(ct).ConfigureAwait(false);

            var url = $"https://{_options.BaseHost}{path}";
            var bodyJson = payload != null ? FourKitesJson.Serialize(payload) : null;

            HttpResponseMessage response = null;
            string responseBody = null;
            Exception transportException = null;

            try
            {
                response = await _retryPolicy.ExecuteAsync(async () =>
                {
                    // Each retry builds a fresh request — HttpRequestMessages can be consumed only once.
                    using (var req = BuildRequest(method, url, bodyJson))
                    {
                        return await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);

                responseBody = response.Content != null
                    ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                    : string.Empty;

                RateLimitTracker.UpdateFromResponse(response);
            }
            catch (Exception ex)
            {
                transportException = ex;
            }

            var result = new FourKitesResponse
            {
                Body = responseBody,
                RateLimitRemaining = RateLimitTracker.Remaining,
                RateLimitLimit = RateLimitTracker.Limit
            };

            if (response == null)
            {
                result.ErrorClass = FourKitesErrorClass.TransportFailure;
                result.TransportException = transportException?.ToString();
                return result;
            }

            result.StatusCode = (int)response.StatusCode;
            result.ErrorClass = FourKitesErrorClassifier.Classify(response, responseBody);

            // Extract requestId from header or body.
            if (response.Headers.TryGetValues("X-Request-Id", out var ridVals))
            {
                foreach (var v in ridVals) { result.RequestId = v; break; }
            }
            if (string.IsNullOrEmpty(result.RequestId) && !string.IsNullOrEmpty(responseBody))
            {
                try
                {
                    var obj = JObject.Parse(responseBody);
                    var rid = obj["requestId"];
                    if (rid != null) result.RequestId = rid.Value<string>();
                    var ec = obj["errorCode"];
                    if (ec != null && ec.Type == JTokenType.Integer) result.ErrorCode = ec.Value<int>();
                    var em = obj["errorMessage"] ?? obj["message"];
                    if (em != null) result.ErrorMessage = em.Value<string>();
                }
                catch { /* not JSON, or malformed — fine */ }
            }

            response.Dispose();
            return result;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, string bodyJson)
        {
            var req = new HttpRequestMessage(method, url);
            req.Headers.Add("apikey", _options.ApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.fourkites.v1+json"));

            if (bodyJson != null)
            {
                req.Content = new StringContent(bodyJson, Encoding.UTF8);
                req.Content.Headers.ContentType = JsonContentType;
            }
            return req;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http?.Dispose();
        }
    }
}
