using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;
using Vendor.Common.Persistence;
using Vendor.FourKites.Webhooks;

namespace Vendor.FourKites.Smoke
{
    internal static class WebhookTests
    {
        public static void RegisterAll()
        {
            // ─── WebhookSignatureValidator ─────────────────────────────────

            TestHarness.Run("Validator. apikey scheme accepts matching header", () =>
            {
                var profileRepo = BuildProfileRepoWithFkConfig(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""baseUrl"": ""https://x"",
                    ""webhookAuth"": {
                        ""scheme"": ""apikey"",
                        ""headerName"": ""X-FK-Webhook-Key"",
                        ""expectedValue"": ""secret-123""
                    }
                }");

                var v = new FourKitesWebhookSignatureValidator(profileRepo, null);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-FK-Webhook-Key"] = "secret-123"
                };
                TestHarness.Assert(v.IsValid(headers, "{}"), "matching apikey should pass");
            });

            TestHarness.Run("Validator. apikey scheme rejects wrong value", () =>
            {
                var profileRepo = BuildProfileRepoWithFkConfig(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""baseUrl"": ""https://x"",
                    ""webhookAuth"": {
                        ""scheme"": ""apikey"",
                        ""headerName"": ""X-FK-Webhook-Key"",
                        ""expectedValue"": ""correct-secret""
                    }
                }");

                var v = new FourKitesWebhookSignatureValidator(profileRepo, null);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-FK-Webhook-Key"] = "wrong-value"
                };
                TestHarness.Assert(!v.IsValid(headers, "{}"), "wrong apikey should fail");
            });

            TestHarness.Run("Validator. basic scheme accepts matching credentials", () =>
            {
                var profileRepo = BuildProfileRepoWithFkConfig(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""baseUrl"": ""https://x"",
                    ""webhookAuth"": {
                        ""scheme"": ""basic"",
                        ""basicUsername"": ""fk_user"",
                        ""basicPassword"": ""fk_pass""
                    }
                }");

                var v = new FourKitesWebhookSignatureValidator(profileRepo, null);
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("fk_user:fk_pass"));
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = "Basic " + b64
                };
                TestHarness.Assert(v.IsValid(headers, "{}"), "valid basic creds should pass");
            });

            TestHarness.Run("Validator. none scheme accepts any request (relies on IP allowlist)", () =>
            {
                var profileRepo = BuildProfileRepoWithFkConfig(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""baseUrl"": ""https://x"",
                    ""webhookAuth"": { ""scheme"": ""none"" }
                }");

                var v = new FourKitesWebhookSignatureValidator(profileRepo, null);
                TestHarness.Assert(v.IsValid(new Dictionary<string, string>(), "{}"),
                    "none scheme should always pass");
            });

            TestHarness.Run("Validator. No profile repository → fail-closed (rejects all)", () =>
            {
                var v = new FourKitesWebhookSignatureValidator();  // parameterless, no repo
                TestHarness.Assert(!v.IsValid(new Dictionary<string, string>(), "{}"),
                    "no profile repo should fail-closed");
            });

            TestHarness.Run("Validator. Constant-time comparison resists length differences", () =>
            {
                var profileRepo = BuildProfileRepoWithFkConfig(@"{
                    ""apiKey"": ""k"", ""billToCode"": ""B"", ""baseUrl"": ""https://x"",
                    ""webhookAuth"": {
                        ""scheme"": ""apikey"",
                        ""headerName"": ""X-Key"",
                        ""expectedValue"": ""longer-secret-value""
                    }
                }");
                var v = new FourKitesWebhookSignatureValidator(profileRepo, null);

                // Length mismatch — should fail
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Key"] = "short"
                };
                TestHarness.Assert(!v.IsValid(headers, "{}"), "length-mismatched apikey should fail");
            });

            // ─── WebhookProcessor.ParseAndExtract ──────────────────────────

            TestHarness.Run("Processor. ParseAndExtract handles LOAD_CREATION payload", () =>
            {
                var p = new FourKitesWebhookProcessor();
                var raw = @"{
                    ""messageType"": ""LOAD_CREATION"",
                    ""requestId"": ""req-abc"",
                    ""loadNumber"": ""LOAD123"",
                    ""fourKitesLoadId"": ""FK-456"",
                    ""isSuccess"": true
                }";

                var meta = p.ParseAndExtract(raw);
                TestHarness.AssertEqual("LOAD_CREATION", meta.MessageType);
                TestHarness.AssertEqual("LOAD123", meta.VectorLoadId);
                TestHarness.AssertEqual("FK-456", meta.VendorLoadId);
                TestHarness.Assert(meta.IsSuccess, "IsSuccess should be true");
            });

            TestHarness.Run("Processor. ParseAndExtract handles failed callback with errors", () =>
            {
                var p = new FourKitesWebhookProcessor();
                var raw = @"{
                    ""messageType"": ""STATUS_UPDATE"",
                    ""loadNumber"": ""LOAD999"",
                    ""isSuccess"": false,
                    ""errors"": [{""code"":""INVALID_STATUS"",""msg"":""unknown code""}]
                }";

                var meta = p.ParseAndExtract(raw);
                TestHarness.AssertEqual("STATUS_UPDATE", meta.MessageType);
                TestHarness.AssertEqual("LOAD999", meta.VectorLoadId);
                TestHarness.Assert(!meta.IsSuccess, "IsSuccess should be false");
                TestHarness.AssertContains(meta.ErrorsJson, "INVALID_STATUS", "errors captured");
            });

            TestHarness.Run("Processor. ParseAndExtract handles malformed JSON without throwing", () =>
            {
                var p = new FourKitesWebhookProcessor();
                // Must not throw
                var meta = p.ParseAndExtract("this is not json");
                TestHarness.AssertNotNull(meta, "should return non-null metadata");
            });

            TestHarness.Run("Processor. ParseAndExtract on null/empty returns IsSuccess=true default", () =>
            {
                var p = new FourKitesWebhookProcessor();
                var meta1 = p.ParseAndExtract(null);
                TestHarness.AssertNotNull(meta1, "null input → non-null metadata");
                var meta2 = p.ParseAndExtract("");
                TestHarness.AssertNotNull(meta2, "empty input → non-null metadata");
            });

            // ─── WebhookProcessor.FindMatchingTransactionAsync ─────────────
            // These tests require the DB. They run only if connectivity works.

            TestHarness.RunAsync("Processor. FindMatching matches by requestId when present", async () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
                var outboundRepo = new OutboundTransactionRepository(connStr);

                // Insert an outbound transaction with a known VendorRequestId
                var loadId = $"FK_SMOKE_{DateTime.UtcNow.Ticks}";
                var evt = new LoadCreatedEvent
                    { VectorLoadId = loadId, SourceSystem = "Test", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");

                var result = VendorOperationResult.Succeeded(202, vendorRequestId: "req-find-1");
                await outboundRepo.RecordOutcomeAsync(txId, result);

                // Build a callback row with that same requestId in the raw payload
                var callback = new InboundCallbackRow
                {
                    VendorName = "FourKites",
                    VectorLoadId = loadId,
                    RawPayload = @"{""requestId"":""req-find-1"",""loadNumber"":""" + loadId + @"""}"
                };

                var processor = new FourKitesWebhookProcessor();
                using (var cn = new SqlConnection(connStr))
                {
                    await cn.OpenAsync();
                    var matched = await processor.FindMatchingTransactionAsync(callback, cn, CancellationToken.None);
                    TestHarness.AssertEqual(txId, matched.Value, "should match by requestId");
                }

                // Cleanup
                await CleanupOutbound(loadId);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Processor. FindMatching falls back to loadNumber when no requestId", async () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
                var outboundRepo = new OutboundTransactionRepository(connStr);

                var loadId = $"FK_SMOKE_{DateTime.UtcNow.Ticks}";
                var evt = new LoadCreatedEvent
                    { VectorLoadId = loadId, SourceSystem = "Test", Mode = "TL" };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                // Callback with NO requestId — must match by loadNumber instead
                var callback = new InboundCallbackRow
                {
                    VendorName = "FourKites",
                    VectorLoadId = loadId,
                    RawPayload = @"{""loadNumber"":""" + loadId + @"""}"
                };

                var processor = new FourKitesWebhookProcessor();
                using (var cn = new SqlConnection(connStr))
                {
                    await cn.OpenAsync();
                    var matched = await processor.FindMatchingTransactionAsync(callback, cn, CancellationToken.None);
                    TestHarness.AssertEqual(txId, matched.Value, "should match by loadNumber fallback");
                }

                await CleanupOutbound(loadId);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Processor. FindMatching returns null when no match", async () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];

                var callback = new InboundCallbackRow
                {
                    VendorName = "FourKites",
                    VectorLoadId = $"FK_SMOKE_NONEXISTENT_{DateTime.UtcNow.Ticks}",
                    RawPayload = @"{""loadNumber"":""never-dispatched""}"
                };

                var processor = new FourKitesWebhookProcessor();
                using (var cn = new SqlConnection(connStr))
                {
                    await cn.OpenAsync();
                    var matched = await processor.FindMatchingTransactionAsync(callback, cn, CancellationToken.None);
                    TestHarness.AssertNull(matched, "should return null for unknown load");
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Processor. OnConfirmedAsync writes cross-reference for LOAD_CREATION", async () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
                var inboundRepo = new InboundCallbackRepository(connStr);
                var processor = new FourKitesWebhookProcessor(null, null, inboundRepo);

                var loadId = $"FK_SMOKE_XREF_{DateTime.UtcNow.Ticks}";
                var callback = new InboundCallbackRow
                {
                    VendorName = "FourKites",
                    VectorLoadId = loadId,
                    VendorLoadId = "FK-XREF-789",
                    MessageType = "LOAD_CREATION"
                };

                using (var cn = new SqlConnection(connStr))
                {
                    await cn.OpenAsync();
                    await processor.OnConfirmedAsync(callback, matchedTransactionId: 999, cn, CancellationToken.None);

                    // Verify cross-reference exists
                    using (var cmd = new SqlCommand(
                        "SELECT COUNT(*) FROM dbo.LoadCrossReference WHERE VectorLoadId = @V AND VendorName = 'FourKites';", cn))
                    {
                        cmd.Parameters.AddWithValue("@V", loadId);
                        var count = (int)await cmd.ExecuteScalarAsync();
                        TestHarness.AssertEqual(1, count, "cross-ref should be written");
                    }

                    // Cleanup
                    using (var cmd = new SqlCommand(
                        "DELETE FROM dbo.LoadCrossReference WHERE VectorLoadId = @V;", cn))
                    {
                        cmd.Parameters.AddWithValue("@V", loadId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Processor. OnConfirmedAsync skips non-LOAD_CREATION messages (no xref)", async () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
                var inboundRepo = new InboundCallbackRepository(connStr);
                var processor = new FourKitesWebhookProcessor(null, null, inboundRepo);

                var loadId = $"FK_SMOKE_NOXREF_{DateTime.UtcNow.Ticks}";
                var callback = new InboundCallbackRow
                {
                    VendorName = "FourKites",
                    VectorLoadId = loadId,
                    VendorLoadId = "FK-NOXREF-1",
                    MessageType = "STATUS_UPDATE"  // not LOAD_CREATION
                };

                using (var cn = new SqlConnection(connStr))
                {
                    await cn.OpenAsync();
                    await processor.OnConfirmedAsync(callback, 999, cn, CancellationToken.None);

                    using (var cmd = new SqlCommand(
                        "SELECT COUNT(*) FROM dbo.LoadCrossReference WHERE VectorLoadId = @V;", cn))
                    {
                        cmd.Parameters.AddWithValue("@V", loadId);
                        var count = (int)await cmd.ExecuteScalarAsync();
                        TestHarness.AssertEqual(0, count,
                            "no xref should be written for non-LOAD_CREATION messages");
                    }
                }
            }).GetAwaiter().GetResult();

            // Cleanup: restore any FK profiles we deactivated during validator tests.
            // Also remove the WEBHOOK_TEST row so reruns start clean.
            TestHarness.Run("Cleanup. Restore seed FK profiles + remove WEBHOOK_TEST row", () =>
            {
                var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
                using (var cn = new SqlConnection(connStr))
                using (var cmd = new SqlCommand(@"
UPDATE dbo.ClientProfiles
SET IsActive = 1, UpdatedUtc = SYSUTCDATETIME()
WHERE VendorName = 'FourKites' AND ShipperCode <> 'WEBHOOK_TEST' AND IsActive = 0;

DELETE FROM dbo.ClientProfiles WHERE ShipperCode = 'WEBHOOK_TEST';", cn))
                {
                    cn.Open();
                    cmd.ExecuteNonQuery();
                }
            });
        }

        // ─── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates a ClientProfileRepository backed by a real DB connection. The caller
        /// is responsible for ensuring the profile row exists; use UpsertTestProfile() to
        /// install a synthetic row before the test, RemoveTestProfile() to clean up.
        ///
        /// Cache TTL is 0 so every call re-reads the DB — keeps tests independent.
        /// </summary>
        private static ClientProfileRepository BuildProfileRepoWithFkConfig(string configJson)
        {
            var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
            UpsertTestProfile(connStr, configJson);
            return new ClientProfileRepository(connStr, TimeSpan.Zero);
        }

        /// <summary>
        /// Installs a test ClientProfile row identified by ShipperCode='WEBHOOK_TEST'.
        /// Upsert so repeated test runs don't conflict.
        ///
        /// Also TEMPORARILY DEACTIVATES other FK profiles so the validator's
        /// "first active FK profile wins" lookup deterministically finds ours.
        /// The cleanup helper at the end of the run reactivates them.
        /// </summary>
        private static void UpsertTestProfile(string connStr, string configJson)
        {
            const string sql = @"
-- Deactivate other FK profiles so the validator picks ours unambiguously
UPDATE dbo.ClientProfiles
SET IsActive = 0, UpdatedUtc = SYSUTCDATETIME()
WHERE VendorName = 'FourKites'
  AND ShipperCode <> 'WEBHOOK_TEST'
  AND IsActive = 1;

IF EXISTS (SELECT 1 FROM dbo.ClientProfiles WHERE ShipperCode = 'WEBHOOK_TEST' AND VendorName = 'FourKites')
    UPDATE dbo.ClientProfiles
    SET ConfigJson = @ConfigJson, IsActive = 1, UpdatedUtc = SYSUTCDATETIME()
    WHERE ShipperCode = 'WEBHOOK_TEST' AND VendorName = 'FourKites';
ELSE
    INSERT INTO dbo.ClientProfiles (ShipperCode, VendorName, IsActive, EnabledEvents, ConfigJson, CreatedUtc, UpdatedUtc)
    VALUES ('WEBHOOK_TEST', 'FourKites', 1, 'LoadCreatedEvent', @ConfigJson, SYSUTCDATETIME(), SYSUTCDATETIME());";

            using (var cn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.Add("@ConfigJson", System.Data.SqlDbType.NVarChar, -1).Value = configJson;
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static async Task CleanupOutbound(string loadId)
        {
            var connStr = ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"];
            using (var cn = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(
                "DELETE FROM dbo.VendorOutboundTransactions WHERE VectorLoadId = @V;", cn))
            {
                cmd.Parameters.AddWithValue("@V", loadId);
                await cn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
