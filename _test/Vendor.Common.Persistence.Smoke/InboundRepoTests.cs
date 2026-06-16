using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;

namespace Vendor.Common.Persistence.Smoke
{
    internal static class InboundRepoTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("Inbound.UpsertAsync inserts a new callback row", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var loadId = DbHelper.NewTestLoadId("upsert1");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\",\"MessageType\":\"LOAD_CREATION\"}}";
                var hash = Sha256(raw);

                var meta = new InboundEventMetadata
                {
                    MessageType = "LOAD_CREATION",
                    VendorLoadId = "FK-INBOUND-1",
                    VectorLoadId = loadId,
                    IsSuccess = true
                };

                var callbackId = await repo.UpsertAsync("FourKites", hash, raw, meta);
                TestHarness.Assert(callbackId > 0, $"expected positive CallbackId, got {callbackId}");

                var msgType = await DbHelper.ReadInboundAsync<string>(callbackId, "MessageType");
                TestHarness.AssertEqual("LOAD_CREATION", msgType);

                var receiptCount = await DbHelper.ReadInboundAsync<int>(callbackId, "ReceiptCount");
                TestHarness.AssertEqual(1, receiptCount);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.UpsertAsync — duplicate hash increments ReceiptCount (dedupe)", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var loadId = DbHelper.NewTestLoadId("dedupe");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var meta = new InboundEventMetadata { VectorLoadId = loadId, IsSuccess = true };

                var firstId = await repo.UpsertAsync("FourKites", hash, raw, meta);
                var secondId = await repo.UpsertAsync("FourKites", hash, raw, meta);

                // Both calls should return the SAME callback id (dedupe worked)
                TestHarness.AssertEqual(firstId, secondId, "duplicate upsert returns same CallbackId");

                // ReceiptCount should now be 2
                var receiptCount = await DbHelper.ReadInboundAsync<int>(firstId, "ReceiptCount");
                TestHarness.AssertEqual(2, receiptCount);

                // Only one row should exist for this load
                var rowCount = await DbHelper.CountRowsAsync("VendorInboundCallbacks", loadId);
                TestHarness.AssertEqual(1, rowCount);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.UpsertAsync — rejects malformed payload hash", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);
                bool threw = false;
                try
                {
                    await repo.UpsertAsync("FourKites", "tooshort", "{}", new InboundEventMetadata());
                }
                catch (ArgumentException) { threw = true; }
                TestHarness.Assert(threw, "expected ArgumentException for non-64-char hash");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.ClaimUnprocessedAsync claims a batch", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);

                // Insert 3 unprocessed callbacks
                var ids = new List<long>();
                for (int i = 0; i < 3; i++)
                {
                    var loadId = DbHelper.NewTestLoadId($"claim{i}");
                    var raw = $"{{\"VectorLoadId\":\"{loadId}\",\"seq\":{i}}}";
                    var hash = Sha256(raw);
                    var meta = new InboundEventMetadata { VectorLoadId = loadId };
                    ids.Add(await repo.UpsertAsync("FourKites", hash, raw, meta));
                }

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    var claimed = await repo.ClaimUnprocessedAsync(cn, batchSize: 100);
                    TestHarness.Assert(claimed.Count >= 3,
                        $"expected at least 3 claimed rows, got {claimed.Count}");
                }

                // After claim, ProcessedUtc on all three should be non-null
                foreach (var id in ids)
                {
                    var processed = await DbHelper.ReadInboundAsync<DateTime?>(id, "ProcessedUtc");
                    TestHarness.AssertNotNull(processed, "ProcessedUtc on claimed row");
                }
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.UnclaimAsync resets ProcessedUtc to NULL", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);

                var loadId = DbHelper.NewTestLoadId("unclaim");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var id = await repo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId });

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    await repo.ClaimUnprocessedAsync(cn, batchSize: 100);  // claims it
                    await repo.UnclaimAsync(cn, id);                       // releases it
                }

                var processed = await DbHelper.ReadInboundAsync<object>(id, "ProcessedUtc");
                TestHarness.AssertNull(processed, "ProcessedUtc should be NULL after unclaim");
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.LinkCorrelatedAsync stamps MatchedTransactionId + status", async () =>
            {
                var inboundRepo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);

                // Set up an outbound transaction first
                var loadId = DbHelper.NewTestLoadId("correlate");
                var evt = new Vendor.Common.Events.LoadCreatedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Persistence.Smoke", Mode = "TL"
                };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");

                // Set up an inbound callback
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var cbId = await inboundRepo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId });

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    await inboundRepo.LinkCorrelatedAsync(cn, cbId, txId);
                }

                var matched = await DbHelper.ReadInboundAsync<long>(cbId, "MatchedTransactionId");
                TestHarness.AssertEqual(txId, matched, "MatchedTransactionId");

                var status = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("MATCHED", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.MarkNoMatchAsync sets CorrelationStatus = NO_MATCH", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);

                var loadId = DbHelper.NewTestLoadId("nomatch");
                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = Sha256(raw);
                var cbId = await repo.UpsertAsync("FourKites", hash, raw,
                    new InboundEventMetadata { VectorLoadId = loadId });

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    await repo.MarkNoMatchAsync(cn, cbId);
                }

                var status = await DbHelper.ReadInboundAsync<string>(cbId, "CorrelationStatus");
                TestHarness.AssertEqual("NO_MATCH", status);
            }).GetAwaiter().GetResult();

            TestHarness.RunAsync("Inbound.RecordCrossReferenceAsync writes via usp_RecordVendorLoadCrossReference", async () =>
            {
                var repo = new InboundCallbackRepository(DbHelper.ConnectionString);
                var loadId = DbHelper.NewTestLoadId("xref");

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    await repo.RecordCrossReferenceAsync(cn, loadId, "FourKites", "FK-XREF-123", "CREATED");
                }

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                using (var cmd = new SqlCommand(
                    "SELECT VendorLoadId, TrackingStatus FROM dbo.LoadCrossReference " +
                    "WHERE VectorLoadId = @V AND VendorName = 'FourKites';", cn))
                {
                    cmd.Parameters.AddWithValue("@V", loadId);
                    await cn.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        TestHarness.Assert(await reader.ReadAsync(), "expected one xref row");
                        TestHarness.AssertEqual("FK-XREF-123", reader.GetString(0));
                        TestHarness.AssertEqual("CREATED", reader.GetString(1));
                    }
                }
            }).GetAwaiter().GetResult();
        }

        private static string Sha256(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
