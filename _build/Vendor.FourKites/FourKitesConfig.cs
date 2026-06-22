using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vendor.FourKites
{
    /// <summary>
    /// Typed view of the ClientProfile.ConfigJson blob for the FourKites vendor.
    ///
    /// Updated per the official FK API spec at docs.fourkites.com/api-reference:
    ///   - Auth header is "apikey" (lowercase, raw, no prefix)
    ///   - Multiple environments (staging / production / azure-staging / azure-production)
    ///   - "billToCode" is no longer a top-level Load Create field per FK spec, but we
    ///     retain it in config for potential future Location Update calls (carrier-side
    ///     endpoint that does take a billToCode)
    ///   - Endpoint paths use FK's actual URLs (/api/v1/tracking, /api/v1/tracking/{loadId},
    ///     /api/v1/tracking/delete_loads, /document-data/upload)
    ///
    /// EXAMPLE ConfigJson:
    /// <code>
    /// {
    ///   "apiKey":      "OFX6BL85E0SC9W9SDHIEWTTPRFH8U",
    ///   "billToCode":  "2215324",
    ///   "vectorScac":  "VCTR",
    ///   "environment": "staging",
    ///   "defaultHaulType": "brokered_load",
    ///   "dispatchPolicy": {
    ///     "verbosity": "Generous"
    ///   },
    ///   "webhookAuth": {
    ///     "scheme":         "apikey",
    ///     "headerName":     "X-FK-Webhook-Key",
    ///     "expectedValue":  "fk-webhook-secret-xyz"
    ///   },
    ///   "rateLimit": {
    ///     "requestsPerSecond": 1,
    ///     "burstSize":         5
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

        /// <summary>The FK API key, sent as the "apikey" request header (lowercase, raw).</summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Vector's billToCode in FK's system. Stored even though it's NOT a Load Create
        /// field per the current FK spec — retained for potential future Location Update
        /// calls (the carrier-facing endpoint where it IS required). Also a useful identifier
        /// for ops/support.
        /// </summary>
        public string BillToCode { get; set; }

        // ─── Optional with defaults ─────────────────────────────────────

        /// <summary>
        /// Vector's SCAC or Service Provider ID — goes into load.carrier on every payload.
        /// FK requires this; rejects loads without it.
        /// Default "VCTR" (placeholder). See open question O-001.
        /// </summary>
        public string VectorScac { get; set; } = "VCTR";

        /// <summary>
        /// Which FK environment to target. Drives BaseUrl resolution.
        /// Valid: "staging" | "production" | "azure-staging" | "azure-production".
        /// Default "staging" — dev work should never accidentally hit production.
        /// </summary>
        public string Environment { get; set; } = "staging";

        /// <summary>
        /// Override the BaseUrl explicitly. If set, takes precedence over Environment.
        /// Useful for pointing at a local mock for tests.
        /// </summary>
        public string BaseUrlOverride { get; set; }

        /// <summary>
        /// Default haulType for Vector's brokered loads. FK requires haulType as an array;
        /// we send [DefaultHaulType]. Configurable in case FK confirms a different value.
        /// See open question O-011.
        /// </summary>
        public string DefaultHaulType { get; set; } = "brokered_load";

        /// <summary>HTTP request timeout. Default 30 seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        // ─── Webhook authentication ─────────────────────────────────────

        public WebhookAuthConfig WebhookAuth { get; set; }

        // ─── Rate limiting ──────────────────────────────────────────────

        /// <summary>
        /// FK documents a 1 req/sec limit on Create. Default to 1/sec with a small burst.
        /// </summary>
        public RateLimitConfig RateLimit { get; set; }

        // ─── Derived ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the FK base URL from BaseUrlOverride or Environment.
        /// </summary>
        [JsonIgnore]
        public string BaseUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(BaseUrlOverride))
                    return BaseUrlOverride.TrimEnd('/');

                var key = (Environment ?? "staging").Trim().ToLowerInvariant();
                if (_environmentHosts.TryGetValue(key, out var host)) return host;

                // Unknown environment — fail loud rather than silently using the wrong host
                throw new FourKitesConfigException(
                    $"Unknown FK environment '{Environment}'. " +
                    "Valid values: " + string.Join(", ", _environmentHosts.Keys));
            }
        }

        // Endpoint paths per FK docs/api-reference
        [JsonIgnore] public string LoadCreateEndpoint   => "/api/v1/tracking";
        public string LoadUpdateEndpoint(long fkLoadId) => "/api/v1/tracking/" + fkLoadId;
        [JsonIgnore] public string LoadDeleteEndpoint   => "/api/v1/tracking/delete_loads";
        [JsonIgnore] public string DocumentUploadEndpoint => "/document-data/upload";

        /// <summary>
        /// Carrier-side Dispatcher Update endpoint. Used for posting in-motion location
        /// reports and status milestones (events) once tracking is active. Vector is the
        /// registered Carrier on these loads, so we push to this endpoint as the carrier.
        /// One endpoint carries locationUpdate, eventUpdate, and other sub-objects — the
        /// adapter chooses which sub-objects to include based on the event type.
        /// </summary>
        [JsonIgnore] public string DispatcherUpdateEndpoint => "/load/update/dispatcher-api/async";

        // ─── Environment URL table ──────────────────────────────────────

        // Per FK docs/api-reference Create Shipment Request panel.
        // Azure-Staging URL not captured verbatim in the docs we have — falls back to
        // a known FK pattern; verify before targeting that env.
        private static readonly IReadOnlyDictionary<string, string> _environmentHosts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["staging"]         = "https://api-staging.fourkites.com",
                ["production"]      = "https://api.fourkites.com",
                ["azure-production"] = "https://api.ng.fourkites.com",
                ["azure-staging"]    = "https://api-staging.ng.fourkites.com"  // O-? not verbatim
            };

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

            // Fill defaults on optional nested objects
            if (cfg.RateLimit == null)
                cfg.RateLimit = new RateLimitConfig { RequestsPerSecond = 1, BurstSize = 5 };

            // Eagerly validate environment maps to a host so a bad value fails at config-parse
            // time rather than at first dispatch
            var _ = cfg.BaseUrl;

            return cfg;
        }
    }

    /// <summary>Webhook authentication settings — how FK proves its identity to us.</summary>
    public class WebhookAuthConfig
    {
        /// <summary>Auth scheme: "apikey" (default), "basic", or "none". O-003 still open.</summary>
        public string Scheme { get; set; } = "apikey";

        /// <summary>Header name FK sends the credential in. For apikey scheme.</summary>
        public string HeaderName { get; set; } = "X-FK-Webhook-Key";

        /// <summary>Expected credential value to match against. For apikey or basic schemes.</summary>
        public string ExpectedValue { get; set; }

        /// <summary>Basic-auth username. Used only when Scheme = "basic".</summary>
        public string BasicUsername { get; set; }

        /// <summary>Basic-auth password. Used only when Scheme = "basic".</summary>
        public string BasicPassword { get; set; }

        /// <summary>
        /// Optional IP allowlist for FK webhook origin. CIDR or plain IPs.
        /// When non-empty, validator additionally checks request origin IP.
        /// FK static IP range is O-004 — leave empty until confirmed.
        /// </summary>
        public List<string> AllowedSourceIps { get; set; }
    }

    /// <summary>Rate-limit settings — how aggressively we throttle ourselves.</summary>
    public class RateLimitConfig
    {
        /// <summary>
        /// Steady-state requests per second. Default 1 (per FK's documented limit on Create).
        /// </summary>
        public int RequestsPerSecond { get; set; } = 1;

        /// <summary>Burst size (token bucket capacity). Default 5.</summary>
        public int BurstSize { get; set; } = 5;
    }

    /// <summary>Thrown when ConfigJson is missing or malformed.</summary>
    public class FourKitesConfigException : Exception
    {
        public FourKitesConfigException(string message) : base(message) { }
        public FourKitesConfigException(string message, Exception inner) : base(message, inner) { }
    }
}
