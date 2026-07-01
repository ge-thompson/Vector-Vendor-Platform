using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;

namespace Vendor.FourKites
{
    /// <summary>
    /// HTTP client wrapping calls to the FourKites REST API.
    ///
    /// Auth: FK's "apikey" header (lowercase, raw key, no prefix).
    ///       Per docs.fourkites.com/api-reference; NOT X-Api-Key or Authorization.
    ///
    /// Methods supported:
    ///   POST   — Load Create, Load Delete (batch), Document Upload
    ///   PATCH  — Load Update (FK loadId in URL path)
    ///
    /// Responsibilities:
    /// - Compose URL from FourKitesConfig.BaseUrl + endpoint path
    /// - Attach the FK API key as the "apikey" header
    /// - POST/PATCH JSON bodies
    /// - Apply Polly retry on TRANSIENT failures only (5xx, timeouts, network errors)
    ///   Do NOT retry 4xx — those are permanent and retrying would just spam FK
    /// - Return a typed FourKitesResponse that the adapter consumes
    ///
    /// One client instance is created per adapter instance (which is one per
    /// process via the registry). HttpClient is reused per FK API documented best
    /// practices — creating new HttpClient per call exhausts socket handles at scale.
    ///
    /// THREAD-SAFE: HttpClient is thread-safe by design; the retry policy is too.
    /// </summary>
    public class FourKitesClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
        private readonly Action<Exception> _onError;
        private bool _disposed;

        // FK's required auth header (per docs.fourkites.com/api-reference).
        // Lowercase. Raw key. No "Bearer", no "X-", no prefix of any kind.
        private const string FK_AUTH_HEADER = "apikey";

        /// <summary>
        /// Production constructor — creates its own HttpClient.
        /// </summary>
        public FourKitesClient(TimeSpan timeout, Action<Exception> errorHandler = null)
            : this(BuildDefaultHandler(), timeout, errorHandler, ownsHandler: true)
        {
        }

        /// <summary>
        /// Test-friendly constructor — caller hands us a HttpMessageHandler so tests
        /// can inject a fake.
        /// </summary>
        public FourKitesClient(HttpMessageHandler handler, TimeSpan timeout,
            Action<Exception> errorHandler = null, bool ownsHandler = true)
        {
            _http = new HttpClient(handler, disposeHandler: ownsHandler)
            {
                Timeout = timeout
            };
            _onError = errorHandler ?? (_ => { });
            _retryPolicy = BuildRetryPolicy();
        }

        /// <summary>
        /// POST a JSON payload to the given endpoint path. Returns a typed response
        /// covering all outcomes (success, transient failure, permanent failure).
        /// NEVER THROWS.
        /// </summary>
        public Task<FourKitesResponse> PostJsonAsync(
            string fullUrl, string apiKey, string jsonBody, CancellationToken cancellationToken)
            => SendJsonAsync(HttpMethod.Post, fullUrl, apiKey, jsonBody, cancellationToken);

        /// <summary>
        /// PATCH a JSON payload to the given endpoint path. Used for FK Load Update,
        /// which takes the FK-issued loadId in the URL path. NEVER THROWS.
        /// </summary>
        public Task<FourKitesResponse> PatchJsonAsync(
            string fullUrl, string apiKey, string jsonBody, CancellationToken cancellationToken)
            => SendJsonAsync(new HttpMethod("PATCH"), fullUrl, apiKey, jsonBody, cancellationToken);

        /// <summary>
        /// GET the given URL with the FK apikey header. No request body. NEVER THROWS.
        /// Returns a FourKitesResponse whose ResponseBody carries the JSON FK returned.
        /// Used by the diagnostic read path (IVendorLoadReader on FourKitesAdapter) and
        /// any future fetch-merge-PATCH flow.
        /// </summary>
        public Task<FourKitesResponse> GetJsonAsync(
            string fullUrl, string apiKey, CancellationToken cancellationToken)
            => SendNoBodyAsync(HttpMethod.Get, fullUrl, apiKey, cancellationToken);

        // ─── Core send logic shared by POST + PATCH ─────────────────────

        private async Task<FourKitesResponse> SendJsonAsync(
            HttpMethod method,
            string fullUrl,
            string apiKey,
            string jsonBody,
            CancellationToken cancellationToken)
        {
            if (_disposed) return FourKitesResponse.OfFailure("Client disposed", "Permanent");

            var sw = Stopwatch.StartNew();

            try
            {
                var policyResult = await _retryPolicy.ExecuteAndCaptureAsync(
                    async (ct) =>
                    {
                        using (var request = new HttpRequestMessage(method, fullUrl))
                        {
                            // FK's auth header — lowercase "apikey", raw key, no prefix.
                            request.Headers.Add(FK_AUTH_HEADER, apiKey);
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            request.Content = new StringContent(jsonBody ?? "", Encoding.UTF8, "application/json");

                            return await _http.SendAsync(request, ct).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                sw.Stop();

                if (policyResult.Outcome == OutcomeType.Failure)
                {
                    var ex = policyResult.FinalException;
                    if (ex == null)
                    {
                        return FourKitesResponse.OfFailure(
                            "Polly outcome=Failure with no exception (unexpected)",
                            "Transient", duration: sw.Elapsed);
                    }

                    _onError(ex);

                    if (ex is OperationCanceledException)
                        return FourKitesResponse.OfFailure("Cancelled before completion", "Transient", duration: sw.Elapsed);

                    return FourKitesResponse.OfFailure(ex.Message, "Transient", duration: sw.Elapsed);
                }

                var httpResponse = policyResult.Result ?? policyResult.FinalHandledResult;
                if (httpResponse == null)
                {
                    return FourKitesResponse.OfFailure(
                        "Polly returned null result with no exception (unexpected)",
                        "Unknown", duration: sw.Elapsed);
                }

                using (httpResponse)
                {
                    string body = "";
                    try
                    {
                        if (httpResponse.Content != null)
                            body = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch { /* body unreadable; carry on with the status code we have */ }

                    var statusCode = (int)httpResponse.StatusCode;

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        // FK Load Create response shape (per docs):
                        //   { "loadId": 1000, "message": "Load created successfully", "statusCode": 200 }
                        // FK Load Update response shape:
                        //   { "message": "Load updated successfully", "requestId": "...", "statusCode": 200 }
                        // FK Load Delete response shape:
                        //   { "message": "Tracking records submitted for deletion", "statusCode": "200", ... }
                        string vendorRequestId = TryExtractField(body, "requestId");
                        string vendorLoadId = TryExtractField(body, "loadId");

                        return FourKitesResponse.OfSuccess(
                            statusCode, body, vendorRequestId, vendorLoadId, sw.Elapsed);
                    }

                    var category = ClassifyStatusCode(statusCode);
                    return FourKitesResponse.OfFailure(
                        $"FK returned HTTP {statusCode}: {Truncate(body, 500)}",
                        category,
                        httpStatusCode: statusCode,
                        responseBody: body,
                        duration: sw.Elapsed);
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return FourKitesResponse.OfFailure("Cancelled", "Transient", duration: sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _onError(ex);
                return FourKitesResponse.OfFailure(ex.Message, "Transient", duration: sw.Elapsed);
            }
        }

        // ─── Core send logic for verbs with no body (GET) ───────────────

        private async Task<FourKitesResponse> SendNoBodyAsync(
            HttpMethod method,
            string fullUrl,
            string apiKey,
            CancellationToken cancellationToken)
        {
            if (_disposed) return FourKitesResponse.OfFailure("Client disposed", "Permanent");

            var sw = Stopwatch.StartNew();

            try
            {
                var policyResult = await _retryPolicy.ExecuteAndCaptureAsync(
                    async (ct) =>
                    {
                        using (var request = new HttpRequestMessage(method, fullUrl))
                        {
                            // FK's auth header — lowercase "apikey", raw key, no prefix.
                            request.Headers.Add(FK_AUTH_HEADER, apiKey);
                            // Accept the FK-versioned media type first (the GET shipment endpoint
                            // documents application/vnd.fourkites.v1+json); fall back to JSON.
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.fourkites.v1+json"));
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            return await _http.SendAsync(request, ct).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                sw.Stop();

                if (policyResult.Outcome == OutcomeType.Failure)
                {
                    var ex = policyResult.FinalException;
                    if (ex == null)
                    {
                        return FourKitesResponse.OfFailure(
                            "Polly outcome=Failure with no exception (unexpected)",
                            "Transient", duration: sw.Elapsed);
                    }

                    _onError(ex);

                    if (ex is OperationCanceledException)
                        return FourKitesResponse.OfFailure("Cancelled before completion", "Transient", duration: sw.Elapsed);

                    return FourKitesResponse.OfFailure(ex.Message, "Transient", duration: sw.Elapsed);
                }

                var httpResponse = policyResult.Result ?? policyResult.FinalHandledResult;
                if (httpResponse == null)
                {
                    return FourKitesResponse.OfFailure(
                        "Polly returned null result with no exception (unexpected)",
                        "Unknown", duration: sw.Elapsed);
                }

                using (httpResponse)
                {
                    string body = "";
                    try
                    {
                        if (httpResponse.Content != null)
                            body = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch { /* body unreadable; carry on with the status code we have */ }

                    var statusCode = (int)httpResponse.StatusCode;

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        // GET responses don't carry a requestId / loadId in our caller's sense —
                        // they ARE the load. Body is returned to the caller for inspection.
                        return FourKitesResponse.OfSuccess(statusCode, body, null, null, sw.Elapsed);
                    }

                    var category = ClassifyStatusCode(statusCode);
                    return FourKitesResponse.OfFailure(
                        $"FK returned HTTP {statusCode}: {Truncate(body, 500)}",
                        category,
                        httpStatusCode: statusCode,
                        responseBody: body,
                        duration: sw.Elapsed);
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return FourKitesResponse.OfFailure("Cancelled", "Transient", duration: sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _onError(ex);
                return FourKitesResponse.OfFailure(ex.Message, "Transient", duration: sw.Elapsed);
            }
        }

        // ─── Polly retry policy ─────────────────────────────────────────────

        /// <summary>
        /// Retry policy: 3 attempts on transient failures (5xx, timeouts, network errors).
        /// Exponential backoff: 200ms, 800ms, 3200ms (×4 per retry).
        ///
        /// Crucially does NOT retry on 4xx — those are permanent client errors and
        /// retrying would just hammer FK with the same bad payload.
        /// </summary>
        private static AsyncRetryPolicy<HttpResponseMessage> BuildRetryPolicy()
        {
            return Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>(ex => !(ex is OperationCanceledException oce && oce.CancellationToken.IsCancellationRequested))
                .Or<IOException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(
                    retryCount: 2,  // = 3 total attempts including initial
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(4, attempt - 1)));
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private static HttpClientHandler BuildDefaultHandler()
        {
            return new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
        }

        private static string ClassifyStatusCode(int statusCode)
        {
            if (statusCode == 429) return "RateLimit";
            if (statusCode >= 500) return "Transient";
            if (statusCode >= 400) return "Permanent";
            return "Unknown";
        }

        private static string TryExtractField(string body, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var jo = JObject.Parse(body);
                var token = jo[fieldName];
                return token?.ToString();
            }
            catch { return null; }
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http?.Dispose();
        }
    }

    /// <summary>
    /// Typed result of a FourKites HTTP call. Returned from <see cref="FourKitesClient"/> methods.
    /// The adapter translates this into a Vendor.Common.VendorOperationResult.
    /// </summary>
    public class FourKitesResponse
    {
        public bool IsSuccess { get; set; }
        public int? HttpStatusCode { get; set; }
        public string ResponseBody { get; set; }
        public string VendorRequestId { get; set; }
        public string VendorLoadId { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorCategory { get; set; }
        public TimeSpan Duration { get; set; }

        public static FourKitesResponse OfSuccess(
            int httpStatusCode, string responseBody, string vendorRequestId, string vendorLoadId, TimeSpan duration)
            => new FourKitesResponse
            {
                IsSuccess = true,
                HttpStatusCode = httpStatusCode,
                ResponseBody = responseBody,
                VendorRequestId = vendorRequestId,
                VendorLoadId = vendorLoadId,
                Duration = duration
            };

        public static FourKitesResponse OfFailure(
            string message, string category,
            int? httpStatusCode = null, string responseBody = null, TimeSpan duration = default(TimeSpan))
            => new FourKitesResponse
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCategory = category,
                HttpStatusCode = httpStatusCode,
                ResponseBody = responseBody,
                Duration = duration
            };
    }
}
