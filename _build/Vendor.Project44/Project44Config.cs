using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vendor.Project44
{
    public class Project44Config
    {
        // OAuth 2 credentials
        public string OauthTokenEndpoint { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }

        // Carrier identity
        public string CarrierIdentifier { get; set; }
        public string CarrierIdentifierType { get; set; } = "SCAC";

        // Environment / routing
        public string Environment { get; set; } = "staging";
        public string BaseUrlOverride { get; set; }
        public int TimeoutSeconds { get; set; } = 30;

        // Rate limiting
        public RateLimitConfig RateLimit { get; set; }

        [JsonIgnore]
        public string BaseUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(BaseUrlOverride))
                    return BaseUrlOverride.TrimEnd('/');

                var key = (Environment ?? "staging").Trim().ToLowerInvariant();
                if (_environmentHosts.TryGetValue(key, out var host)) return host;

                throw new Project44ConfigException(
                    $"Unknown P44 environment '{Environment}'. Set 'baseUrlOverride' for region-specific hosts.");
            }
        }

        [JsonIgnore] public string StatusUpdatesEndpoint => "/api/v4/capacityproviders/tl/shipments/statusUpdates";
        [JsonIgnore] public string ShipmentCreateEndpoint => "/api/v4/capacityproviders/tl/shipments";

        private static readonly IReadOnlyDictionary<string, string> _environmentHosts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["staging"]    = "https://na12.api.project44.com",
                ["production"] = "https://na12.api.project44.com"
            };

        public static Project44Config ParseFrom(string configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
                throw new Project44ConfigException("ConfigJson is empty.");

            Project44Config cfg;
            try
            {
                cfg = JObject.Parse(configJson).ToObject<Project44Config>();
                if (cfg == null) throw new Project44ConfigException("Failed to deserialize ConfigJson.");
            }
            catch (JsonException ex)
            {
                throw new Project44ConfigException("ConfigJson is not valid JSON: " + ex.Message, ex);
            }

            if (string.IsNullOrWhiteSpace(cfg.OauthTokenEndpoint))
                throw new Project44ConfigException("Missing 'oauthTokenEndpoint'.");
            if (string.IsNullOrWhiteSpace(cfg.ClientId))
                throw new Project44ConfigException("Missing 'clientId'.");
            if (string.IsNullOrWhiteSpace(cfg.ClientSecret))
                throw new Project44ConfigException("Missing 'clientSecret'.");
            if (string.IsNullOrWhiteSpace(cfg.CarrierIdentifier))
                throw new Project44ConfigException("Missing 'carrierIdentifier'.");

            if (cfg.RateLimit == null)
                cfg.RateLimit = new RateLimitConfig { RequestsPerMinute = 60 };

            var _ = cfg.BaseUrl;
            return cfg;
        }
    }

    public class RateLimitConfig
    {
        public int RequestsPerMinute { get; set; } = 60;
    }

    public class Project44ConfigException : Exception
    {
        public Project44ConfigException(string message) : base(message) { }
        public Project44ConfigException(string message, Exception inner) : base(message, inner) { }
    }
}