using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Security;

namespace Vendor.Project44
{
    public sealed class Project44Client
    {
        private readonly HttpClient _httpClient;
        private readonly OAuth2ClientCredentialsProvider _tokenProvider;
        private readonly string _baseUrl;

        public Project44Client(HttpClient httpClient, OAuth2ClientCredentialsProvider tokenProvider, string baseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? throw new ArgumentException("baseUrl required", nameof(baseUrl))
                : baseUrl.TrimEnd('/');
        }

        public async Task<HttpCallResult> PostJsonAsync(string path, string jsonBody, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                return HttpCallResult.Failure("Endpoint path was empty.", jsonBody);

            var url = _baseUrl + (path.StartsWith("/") ? path : "/" + path);

            var result = await SendOnceAsync(url, jsonBody, ct).ConfigureAwait(false);

            if (result.HttpStatusCode == 401)
            {
                _tokenProvider.InvalidateToken();
                result = await SendOnceAsync(url, jsonBody, ct).ConfigureAwait(false);
            }

            return result;
        }

        private async Task<HttpCallResult> SendOnceAsync(string url, string jsonBody, CancellationToken ct)
        {
            string token;
            try
            {
                token = await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            }
            catch (OAuthException ex)
            {
                return HttpCallResult.Failure("OAuth token fetch failed: " + ex.Message, jsonBody);
            }
            catch (Exception ex)
            {
                return HttpCallResult.Failure("OAuth token fetch threw: " + ex.Message, jsonBody);
            }

            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            using (var content = new StringContent(jsonBody ?? "", Encoding.UTF8, "application/json"))
            {
                req.Content = content;
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage resp;
                try
                {
                    resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return HttpCallResult.Failure("Request timed out or was cancelled.", jsonBody);
                }
                catch (HttpRequestException ex)
                {
                    return HttpCallResult.Failure("HTTP transport error: " + ex.Message, jsonBody);
                }
                catch (Exception ex)
                {
                    return HttpCallResult.Failure("Unexpected error: " + ex.Message, jsonBody);
                }

                using (resp)
                {
                    string body = resp.Content != null
                        ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false)
                        : string.Empty;

                    return new HttpCallResult
                    {
                        Success = resp.IsSuccessStatusCode,
                        HttpStatusCode = (int)resp.StatusCode,
                        RequestPayloadJson = jsonBody,
                        ResponseBodyJson = body,
                        ErrorMessage = resp.IsSuccessStatusCode ? null
                            : $"P44 returned HTTP {(int)resp.StatusCode}: {Truncate(body, 400)}"
                    };
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }

    public sealed class HttpCallResult
    {
        public bool   Success            { get; set; }
        public int?   HttpStatusCode     { get; set; }
        public string RequestPayloadJson { get; set; }
        public string ResponseBodyJson   { get; set; }
        public string ErrorMessage       { get; set; }

        internal static HttpCallResult Failure(string message, string requestPayload)
        {
            return new HttpCallResult
            {
                Success = false,
                HttpStatusCode = null,
                RequestPayloadJson = requestPayload,
                ResponseBodyJson = null,
                ErrorMessage = message
            };
        }
    }
}