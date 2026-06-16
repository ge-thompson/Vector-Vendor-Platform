using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vendor.FourKites
{
    /// <summary>
    /// Typed view of the ClientProfile.ConfigJson blob for the FourKites vendor.
    ///
    /// EXAMPLE ConfigJson:
    /// <code>
    /// {
    ///   "apiKey": "fk-key-abc123",
    ///   "billToCode": "VECTOR",
    ///   "baseUrl": "https://api.fourkites.com",
    ///   "loadEndpoint": "/v1/loads",
    ///   "webhookAuth": {
    ///     "scheme": "apikey",
    ///     "headerName": "X-FK-Webhook-Key",
    ///     "expectedValue": "fk-webhook-secret-xyz"
    ///   },
    ///   "rateLimit": {
    ///     "requestsPerSecond": 10,
    ///     "burstSize": 20
    ///   }
    /// }
    /// </code>
    ///
    /// The adapter calls <see cref="ParseFrom"/> on every dispatch (cheap because
    /// it's just JSON parsing) to get the current view of config. This lets operators
    /// rotate keys / change endpoints by editing one DB row — no app restart needed.
    /// </summary>
    public class FourKitesConfig
    {
        // ─── Required ────────────────────────────────────────────────────

        /// <summary>The FK API key, sent as a request header.</summary>
        public string ApiKey { get; set; }

        /// <summary>Vector's billToCode in FK's system. Required on most FK payloads.</summary>
        public string BillToCode { get; set; }

        /// <summary>Base URL for the FK API. Typically "https://api.fourkites.com".</summary>
        public string BaseUrl { get; set; }

        // ─── Optional with defaults ─────────────────────────────────────

        /// <summary>Endpoint path for load operations. Default "/v1/loads".</summary>
        public string LoadEndpoint { get; set; } = "/v1/loads";

        /// <summary>Endpoint path for status updates. Default "/v1/loads/status".</summary>
        public string StatusEndpoint { get; set; } = "/v1/loads/status";

        /// <summary>Endpoint path for location updates. Default "/v1/loads/location".</summary>
        public string LocationEndpoint { get; set; } = "/v1/loads/location";

        /// <summary>Endpoint path for document uploads. Default "/v1/loads/documents".</summary>
        public string DocumentEndpoint { get; set; } = "/v1/loads/documents";

        /// <summary>HTTP request timeout. Default 30 seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        // ─── Webhook authentication ─────────────────────────────────────

        public WebhookAuthConfig WebhookAuth { get; set; }

        // ─── Rate limiting ──────────────────────────────────────────────

        public RateLimitConfig RateLimit { get; set; }

        // ─── Parsing ────────────────────────────────────────────────────

        /// <summary>
        /// Parses a ClientProfile.ConfigJson string into a typed FourKitesConfig.
        /// Throws <see cref="FourKitesConfigException"/> if required fields are missing.
        /// </summary>
        public static FourKitesConfig ParseFrom(string configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
                throw new FourKitesConfigException(
                    "ConfigJson is empty. ClientProfile.ConfigJson must contain FK settings.");

            FourKitesConfig cfg;
            try
            {
                // Use a JObject intermediary so we can pick defaults for missing fields
                // without Newtonsoft constructing them as null.
                var jo = JObject.Parse(configJson);
                cfg = jo.ToObject<FourKitesConfig>();
                if (cfg == null)
                    throw new FourKitesConfigException("Failed to deserialize ConfigJson.");
            }
            catch (JsonException ex)
            {
                throw new FourKitesConfigException(
                    "ConfigJson is not valid JSON: " + ex.Message, ex);
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
                throw new FourKitesConfigException("ConfigJson missing required field 'apiKey'.");
            if (string.IsNullOrWhiteSpace(cfg.BillToCode))
                throw new FourKitesConfigException("ConfigJson missing required field 'billToCode'.");
            if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
                throw new FourKitesConfigException("ConfigJson missing required field 'baseUrl'.");

            // Normalize: strip trailing slash from BaseUrl so we can concat with "/v1/..." cleanly
            cfg.BaseUrl = cfg.BaseUrl.TrimEnd('/');

            // Fill defaults on optional nested objects
            if (cfg.RateLimit == null)
                cfg.RateLimit = new RateLimitConfig { RequestsPerSecond = 10, BurstSize = 20 };

            return cfg;
        }
    }

    /// <summary>Webhook authentication settings — how FK proves its identity to us.</summary>
    public class WebhookAuthConfig
    {
        /// <summary>Auth scheme: "apikey" (default), "basic", or "none". O-001 still open.</summary>
        public string Scheme { get; set; } = "apikey";

        /// <summary>Header name FK sends the credential in. For apikey scheme.</summary>
        public string HeaderName { get; set; } = "X-FK-Webhook-Key";

        /// <summary>Expected credential value to match against. For apikey or basic schemes.</summary>
        public string ExpectedValue { get; set; }

        /// <summary>Basic-auth username. Used only when Scheme = "basic".</summary>
        public string BasicUsername { get; set; }

        /// <summary>Basic-auth password. Used only when Scheme = "basic".</summary>
        public string BasicPassword { get; set; }
    }

    /// <summary>Rate-limit settings — how aggressively we throttle ourselves.</summary>
    public class RateLimitConfig
    {
        /// <summary>Steady-state requests per second. Default 10.</summary>
        public int RequestsPerSecond { get; set; } = 10;

        /// <summary>Burst size (token bucket capacity). Default 20.</summary>
        public int BurstSize { get; set; } = 20;
    }

    /// <summary>Thrown when ConfigJson is missing or malformed.</summary>
    public class FourKitesConfigException : Exception
    {
        public FourKitesConfigException(string message) : base(message) { }
        public FourKitesConfigException(string message, Exception inner) : base(message, inner) { }
    }
}
