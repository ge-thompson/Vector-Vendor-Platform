namespace Vendor.FourKites.Smoke
{
    /// <summary>
    /// Smoke tests for FourKitesConfig after the FK spec rewrite.
    /// Covers: multi-environment URL resolution, required-field validation,
    /// defaults for new optional fields (vectorScac, environment, defaultHaulType),
    /// and BaseUrlOverride precedence.
    /// </summary>
    internal static class ConfigTests
    {
        public static void RegisterAll()
        {
            TestHarness.Run("Config. Parses full ConfigJson into typed object", () =>
            {
                var json = @"{
                    ""apiKey"": ""OFX6BL85E0SC9W9SDHIEWTTPRFH8U"",
                    ""billToCode"": ""2215324"",
                    ""vectorScac"": ""VCTR"",
                    ""environment"": ""staging"",
                    ""defaultHaulType"": ""brokered_load"",
                    ""webhookAuth"": {
                        ""scheme"": ""apikey"",
                        ""headerName"": ""X-FK-Webhook-Key"",
                        ""expectedValue"": ""webhook-secret""
                    },
                    ""rateLimit"": { ""requestsPerSecond"": 1, ""burstSize"": 5 }
                }";

                var cfg = FourKitesConfig.ParseFrom(json);
                TestHarness.AssertEqual("OFX6BL85E0SC9W9SDHIEWTTPRFH8U", cfg.ApiKey);
                TestHarness.AssertEqual("2215324", cfg.BillToCode);
                TestHarness.AssertEqual("VCTR", cfg.VectorScac);
                TestHarness.AssertEqual("staging", cfg.Environment);
                TestHarness.AssertEqual("brokered_load", cfg.DefaultHaulType);
                TestHarness.AssertEqual("apikey", cfg.WebhookAuth.Scheme);
                TestHarness.AssertEqual("X-FK-Webhook-Key", cfg.WebhookAuth.HeaderName);
                TestHarness.AssertEqual(1, cfg.RateLimit.RequestsPerSecond);
                TestHarness.AssertEqual(5, cfg.RateLimit.BurstSize);
            });

            TestHarness.Run("Config. environment=staging resolves to api-staging.fourkites.com", () =>
            {
                var cfg = FourKitesConfig.ParseFrom(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""environment"": ""staging""
                }");
                TestHarness.AssertEqual("https://api-staging.fourkites.com", cfg.BaseUrl);
            });

            TestHarness.Run("Config. environment=production resolves to api.fourkites.com", () =>
            {
                var cfg = FourKitesConfig.ParseFrom(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""environment"": ""production""
                }");
                TestHarness.AssertEqual("https://api.fourkites.com", cfg.BaseUrl);
            });

            TestHarness.Run("Config. environment=azure-production resolves to api.ng.fourkites.com", () =>
            {
                var cfg = FourKitesConfig.ParseFrom(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""environment"": ""azure-production""
                }");
                TestHarness.AssertEqual("https://api.ng.fourkites.com", cfg.BaseUrl);
            });

            TestHarness.Run("Config. BaseUrlOverride wins over Environment", () =>
            {
                var cfg = FourKitesConfig.ParseFrom(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"",
                    ""environment"": ""production"",
                    ""baseUrlOverride"": ""http://localhost:9000""
                }");
                TestHarness.AssertEqual("http://localhost:9000", cfg.BaseUrl,
                    "override should take precedence over environment");
            });

            TestHarness.Run("Config. Unknown environment throws on parse", () =>
            {
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(@"{
                        ""apiKey"": ""k"", ""billToCode"": ""B"", ""environment"": ""bogus""
                    }"),
                    "unknown environment should fail at parse time");
            });

            TestHarness.Run("Config. Fills defaults for missing optional fields", () =>
            {
                var json = @"{ ""apiKey"": ""k"", ""billToCode"": ""B"" }";
                var cfg = FourKitesConfig.ParseFrom(json);
                TestHarness.AssertEqual("VCTR", cfg.VectorScac, "default vectorScac");
                TestHarness.AssertEqual("staging", cfg.Environment, "default environment");
                TestHarness.AssertEqual("brokered_load", cfg.DefaultHaulType, "default haulType");
                TestHarness.AssertEqual(30, cfg.TimeoutSeconds, "default timeout");
                TestHarness.AssertNotNull(cfg.RateLimit, "rateLimit defaulted");
                TestHarness.AssertEqual(1, cfg.RateLimit.RequestsPerSecond, "default 1/sec per FK Create limit");
                TestHarness.AssertEqual(5, cfg.RateLimit.BurstSize, "default burst");
            });

            TestHarness.Run("Config. Endpoint paths are FK-spec correct", () =>
            {
                var cfg = FourKitesConfig.ParseFrom(@"{ ""apiKey"": ""k"", ""billToCode"": ""B"" }");
                TestHarness.AssertEqual("/api/v1/tracking", cfg.LoadCreateEndpoint);
                TestHarness.AssertEqual("/api/v1/tracking/12345", cfg.LoadUpdateEndpoint(12345));
                TestHarness.AssertEqual("/api/v1/tracking/delete_loads", cfg.LoadDeleteEndpoint);
                TestHarness.AssertEqual("/document-data/upload", cfg.DocumentUploadEndpoint);
            });

            TestHarness.Run("Config. Throws on missing required apiKey", () =>
            {
                var json = @"{ ""billToCode"": ""B"" }";
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(json),
                    "missing apiKey should throw");
            });

            TestHarness.Run("Config. Throws on missing required billToCode", () =>
            {
                var json = @"{ ""apiKey"": ""k"" }";
                TestHarness.AssertThrows<FourKitesConfigException>(
                    () => FourKitesConfig.ParseFrom(json),
                    "missing billToCode should throw");
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
