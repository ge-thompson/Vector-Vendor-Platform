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
    /// Responsibilities:
    /// - Compose URL from FourKitesConfig.BaseUrl + endpoint path
    /// - Attach the FK API key as an Authorization-style header
    /// - POST JSON bodies
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

        /// <summary>
        /// Production constructor — creates its own HttpClient.
        /// </summary>
        public FourKitesClient(TimeSpan timeout, Action<Exception> errorHandler = null)
            : this(BuildDefaultHandler(), timeout, errorHandler, ownsHandler: true)
        {
        }

        /// <summary>
        /// Test-friendly constructor — caller hands us a HttpMessageHandler so tests
        /// can inject a fake. Pattern: <c>new FourKitesClient(new MyFakeHandler(), TimeSpan.FromSeconds(30))</c>.
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
        ///
        /// Headers added automatically:
        ///   - X-FK-API-Key: {apiKey}     (FK authentication)
        ///   - Content-Type: application/json
        ///   - Accept: application/json
        ///
        /// NEVER THROWS — every failure mode lands as a FourKitesResponse with
        /// IsSuccess=false and the right Error category.
        /// </summary>
        public async Task<FourKitesResponse> PostJsonAsync(
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
                        using (var request = new HttpRequestMessage(HttpMethod.Post, fullUrl))
                        {
                            request.Headers.Add("X-FK-API-Key", apiKey);
                            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            request.Content = new StringContent(jsonBody ?? "", Encoding.UTF8, "application/json");

                            // Note: HttpClient.SendAsync throws on transport failures (network errors,
                            // DNS failures, timeouts). Polly's retry catches those and retries.
                            // It does NOT throw on 4xx/5xx — those come back as a response we inspect.
                            return await _http.SendAsync(request, ct).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                sw.Stop();

                // Polly's ExecuteAndCapture wraps the outcome — if all retries failed
                // with an exception, FinalException is set.
                if (policyResult.Outcome == OutcomeType.Failure)
                {
                    var ex = policyResult.FinalException;
                    // Defensive: Polly may report Failure with a null FinalException in some
                    // cancellation edge cases. Treat as a generic transient failure.
                    if (ex == null)
                    {
                        return FourKitesResponse.OfFailure(
                            "Polly outcome=Failure with no exception (unexpected)",
                            "Transient", duration: sw.Elapsed);
                    }

                    _onError(ex);

                    // Cancellation is not a transport error
                    if (ex is OperationCanceledException)
                        return FourKitesResponse.OfFailure("Cancelled before completion", "Transient", duration: sw.Elapsed);

                    return FourKitesResponse.OfFailure(ex.Message, "Transient", duration: sw.Elapsed);
                }

                // Got an HTTP response — could still be 4xx or 5xx
                // Polly puts the response in Result for unhandled outcomes (success or 4xx),
                // or in FinalHandledResult when the predicate matched but retries exhausted (e.g.,
                // all attempts returned 5xx). Either way, we want the underlying HttpResponseMessage.
                var httpResponse = policyResult.Result ?? policyResult.FinalHandledResult;
                if (httpResponse == null)
                {
                    // Defensive: should never happen if Outcome != Failure, but guard anyway
                    return FourKitesResponse.OfFailure(
                        "Polly returned null result with no exception (unexpected)",
                        "Unknown", duration: sw.Elapsed);
                }

                using (httpResponse)
                {
                    string body = "";
                    try
                    {
                        // Defensive: HttpResponseMessage.Content can be null for some responses
                        // (e.g., 204 No Content, or responses constructed without Content set)
                        if (httpResponse.Content != null)
                            body = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                    catch { /* body unreadable; carry on with the status code we have */ }

                    var statusCode = (int)httpResponse.StatusCode;

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        // Try to extract FK's requestId from the body for audit. FK convention:
                        // their response echoes our requestId, sometimes also assigning a vendor-side id.
                        string vendorRequestId = TryExtractField(body, "requestId");
                        string vendorLoadId = TryExtractField(body, "fourKitesLoadId")
                                              ?? TryExtractField(body, "loadId");

                        return FourKitesResponse.OfSuccess(
                            statusCode, body, vendorRequestId, vendorLoadId, sw.Elapsed);
                    }

                    // Failed HTTP response
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
                // Defensive — Polly should catch HttpRequestException etc., but if anything
                // else escapes, we still return a Result rather than throw.
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

        /// <summary>Classifies an HTTP status as Transient (retryable) or Permanent.</summary>
        private static string ClassifyStatusCode(int statusCode)
        {
            if (statusCode == 429) return "RateLimit";
            if (statusCode >= 500) return "Transient";
            if (statusCode >= 400) return "Permanent";
            return "Unknown";
        }

        /// <summary>Best-effort extraction of one top-level field from a JSON body.</summary>
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
    /// Typed result of a FourKites HTTP call. Returned from <see cref="FourKitesClient.PostJsonAsync"/>.
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
            int? httpStatusCode = null, string responseBody = null, TimeSpan duration = default)
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
