using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Vendor.Common.Security
{
    /// <summary>
    /// OAuth 2.0 Client Credentials grant provider — fetches, caches, and refreshes
    /// bearer tokens for vendors that use OAuth 2 instead of a static API key.
    /// Vendor-agnostic; Project 44 is the first consumer.
    /// </summary>
    public sealed class OAuth2ClientCredentialsProvider
    {
        private readonly string _tokenEndpoint;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _scope;
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _refreshWindow;
        private readonly Action<Exception> _onError;

        private volatile CachedToken _cached;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

        public OAuth2ClientCredentialsProvider(
            string tokenEndpoint,
            string clientId,
            string clientSecret,
            string scope,
            HttpClient httpClient,
            TimeSpan? refreshWindow = null,
            Action<Exception> errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
                throw new ArgumentException("Token endpoint URL is required.", nameof(tokenEndpoint));
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Client ID is required.", nameof(clientId));
            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new ArgumentException("Client secret is required.", nameof(clientSecret));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));

            _tokenEndpoint = tokenEndpoint;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _scope = scope;
            _httpClient = httpClient;
            _refreshWindow = refreshWindow ?? TimeSpan.FromSeconds(60);
            _onError = errorHandler ?? (_ => { });
        }

        /// <summary>Returns a valid bearer token, fetching a fresh one if needed.</summary>
        public async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            var current = _cached;
            if (current != null && DateTime.UtcNow < current.ExpiresUtc - _refreshWindow)
                return current.AccessToken;

            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                current = _cached;
                if (current != null && DateTime.UtcNow < current.ExpiresUtc - _refreshWindow)
                    return current.AccessToken;

                _cached = await FetchTokenAsync(ct).ConfigureAwait(false);
                return _cached.AccessToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>Forces the cached token to be discarded. Call after a 401 to force a refresh.</summary>
        public void InvalidateToken()
        {
            _cached = null;
        }

        private async Task<CachedToken> FetchTokenAsync(CancellationToken ct)
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            };
            if (!string.IsNullOrEmpty(_scope))
                form.Add(new KeyValuePair<string, string>("scope", _scope));

            using (var body = new FormUrlEncodedContent(form))
            using (var req = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint) { Content = body })
            {
                var basic = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(_clientId + ":" + _clientSecret));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage resp;
                try
                {
                    resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _onError(ex);
                    throw new OAuthException(
                        $"OAuth token request to '{_tokenEndpoint}' threw at transport layer: {ex.Message}", ex);
                }

                using (resp)
                {
                    var responseText = resp.Content != null
                        ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false)
                        : string.Empty;

                    if (!resp.IsSuccessStatusCode)
                    {
                        var msg =
                            $"OAuth token endpoint returned HTTP {(int)resp.StatusCode} " +
                            $"({resp.ReasonPhrase}): {Truncate(responseText, 500)}";
                        _onError(new OAuthException(msg));
                        throw new OAuthException(msg);
                    }

                    JObject jo;
                    try
                    {
                        jo = JObject.Parse(responseText);
                    }
                    catch (Exception ex)
                    {
                        var msg = $"OAuth response body wasn't valid JSON: {Truncate(responseText, 500)}";
                        _onError(new OAuthException(msg, ex));
                        throw new OAuthException(msg, ex);
                    }

                    var accessToken = jo.Value<string>("access_token");
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        var msg = $"OAuth response was missing 'access_token'. Body: {Truncate(responseText, 500)}";
                        _onError(new OAuthException(msg));
                        throw new OAuthException(msg);
                    }

                    int expiresInSeconds = jo.Value<int?>("expires_in") ?? 3600;
                    var expiresUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds);

                    return new CachedToken(accessToken, expiresUtc);
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private sealed class CachedToken
        {
            public string AccessToken { get; }
            public DateTime ExpiresUtc { get; }

            public CachedToken(string accessToken, DateTime expiresUtc)
            {
                AccessToken = accessToken;
                ExpiresUtc = expiresUtc;
            }
        }
    }

    /// <summary>Thrown when an OAuth token fetch fails.</summary>
    public sealed class OAuthException : Exception
    {
        public OAuthException(string message) : base(message) { }
        public OAuthException(string message, Exception inner) : base(message, inner) { }
    }
}