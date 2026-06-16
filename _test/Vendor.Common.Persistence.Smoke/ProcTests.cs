using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Events;

namespace Vendor.Common.Persistence.Smoke
{
    internal static class ProcTests
    {
        public static void RegisterAll()
        {
            TestHarness.RunAsync("usp_GetLoadAuditTrail returns 3 result sets for a load", async () =>
            {
                // Set up: one outbound transaction, one inbound callback, one xref — all for same load
                var loadId = DbHelper.NewTestLoadId("audit");

                var outboundRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var inboundRepo  = new InboundCallbackRepository(DbHelper.ConnectionString);

                var evt = new LoadCreatedEvent
                {
                    VectorLoadId = loadId, SourceSystem = "Persistence.Smoke", Mode = "TL"
                };
                var txId = await outboundRepo.InsertPendingAsync(evt, "FourKites", "VECTOR_DEFAULT");
                await outboundRepo.RecordOutcomeAsync(txId, VendorOperationResult.Succeeded(202));

                var raw = $"{{\"VectorLoadId\":\"{loadId}\"}}";
                var hash = System.Security.Cryptography.SHA256.Create().ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes(raw));
                var hashHex = System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                await inboundRepo.UpsertAsync("FourKites", hashHex, raw,
                    new InboundEventMetadata { VectorLoadId = loadId, MessageType = "LOAD_CREATION" });

                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                {
                    await cn.OpenAsync();
                    await inboundRepo.RecordCrossReferenceAsync(cn, loadId, "FourKites", "FK-AUDIT-1", "CREATED");
                }

                // Now exercise the proc
                using (var cn = new SqlConnection(DbHelper.ConnectionString))
                using (var cmd = new SqlCommand("dbo.usp_GetLoadAuditTrail", cn)
                                   { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@VectorLoadId", loadId);
                    await cn.OpenAsync();

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        // Result set 1: outbound transactions
                        int outboundCount = 0;
                        while (await reader.ReadAsync()) outboundCount++;
                        TestHarness.AssertEqual(1, outboundCount, "outbound result set rows");

                        // Result set 2: inbound callbacks
                        TestHarness.Assert(await reader.NextResultAsync(), "expected 2nd result set");
                        int inboundCount = 0;
                        while (await reader.ReadAsync()) inboundCount++;
                        TestHarness.AssertEqual(1, inboundCount, "inbound result set rows");

                        // Result set 3: cross-references
                        TestHarness.Assert(await reader.NextResultAsync(), "expected 3rd result set");
                        int xrefCount = 0;
                        while (await reader.ReadAsync()) xrefCount++;
                        TestHarness.AssertEqual(1, xrefCount, "xref result set rows");
                    }
                }
            }).GetAwaiter().GetResult();
        }
    }
}
