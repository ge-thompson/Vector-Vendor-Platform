namespace Vendor.FourKites.Smoke
{
    internal static class ConfigTests
    {
        public static void RegisterAll()
        {
            TestHarness.Run("Config. Parses full ConfigJson into typed object", () =>
            {
                var json = @"{
                    ""apiKey"": ""fk-abc123"",
                    ""billToCode"": ""VECTOR"",
                    ""baseUrl"": ""https://api.fourkites.com"",
                    ""loadEndpoint"": ""/v1/loads"",
                    ""webhookAuth"": {
                        ""scheme"": ""apikey"",
                        ""headerName"": ""X-FK-Webhook-Key"",
                        ""expectedValue"": ""webhook-secret""
                    },
                    ""rateLimit"": { ""requestsPerSecond"": 5, ""burstSize"": 12 }
                }";

                var cfg = FourKitesConfig.ParseFrom(json);
                TestHarness.AssertEqual("fk-abc123", cfg.ApiKey);
                TestHarness.AssertEqual("VECTOR", cfg.BillToCode);
                TestHarness.AssertEqual("https://api.fourkites.com", cfg.BaseUrl);
                TestHarness.AssertEqual("/v1/loads", cfg.LoadEndpoint);
                TestHarness.AssertEqual("apikey", cfg.WebhookAuth.Scheme);
                TestHarness.AssertEqual("X-FK-Webhook-Key", cfg.WebhookAuth.HeaderName);
                TestHarness.AssertEqual(5, cfg.RateLimit.RequestsPerSecond);
                TestHarness.AssertEqual(12, cfg.RateLimit.BurstSize);
            });

            TestHarness.Run("Config. Fills defaults for missing optional fields", () =>
            {
                var json = @"{
                    ""apiKey"": ""k"",
                    ""billToCode"": ""B"",
                    ""baseUrl"": ""https://api.fourkites.com""
                }";
                var cfg = FourKitesConfig.ParseFrom(json);
                TestHarness.AssertEqual("/v1/loads", cfg.LoadEndpoint, "default loadEndpoint");
                TestHarness.AssertEqual(30, cfg.TimeoutSeconds, "default timeout");
                TestHarness.AssertNotNull(cfg.RateLimit, "rateLimit defaulted");
                TestHarness.AssertEqual(10, cfg.RateLimit.RequestsPerSecond, "default rps");
                TestHarness.AssertEqual(20, cfg.RateLimit.BurstSize, "default burst");
            });

            TestHarness.Run("Config. Strips trailing slash from BaseUrl", () =>
            {
                var json = @"{
                    ""apiKey"": ""k"",
                    ""billToCode"": ""B"",
                    ""baseUrl"": ""https://api.fourkites.com/""
                }";
                var cfg = FourKitesConfig.ParseFrom(json);
                TestHarness.AssertEqual("https://api.fourkites.com", cfg.BaseUrl,
                    "trailing slash should be stripped");
            });

            TestHarness.Run("Config. Throws on missing required apiKey", () =>
            {
                var json = @"{ ""billToCode"": ""B"", ""baseUrl"": ""https://x"" }";
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(json),
                    "missing apiKey should throw");
            });

            TestHarness.Run("Config. Throws on malformed JSON", () =>
            {
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom("{this is not json"),
                    "malformed JSON should throw FourKitesConfigException");
            });

            TestHarness.Run("Config. Throws on empty input", () =>
            {
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(""),
                    "empty input should throw");
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(null),
                    "null input should throw");
            });
        }
    }
}
