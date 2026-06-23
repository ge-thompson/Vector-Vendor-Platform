using System;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FourKitesIntegration.WebhookReceiver
{
    /// <summary>
    /// Persists inbound webhook callbacks to FourKitesInboundCallbacks.
    /// Includes a dedupe primitive — FourKites may retry callbacks, and we must not double-process.
    /// See SqlMigrations/03_InboundCallbacks.sql for schema.
    /// </summary>
    public class InboundCallbacksRepository
    {
        private readonly string _connectionString;

        public InboundCallbacksRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Insert a new callback row if its dedupe hash hasn't been seen.
        /// Returns true if inserted (new), false if it's a duplicate of a recent callback.
        /// </summary>
        public async Task<bool> InsertIfNewAsync(string messageType, long? fkLoadId, string loadNumber,
            string referenceNumbersJson, string rawPayload)
        {
            var hash = ComputeHash(messageType, fkLoadId, rawPayload);

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.FourKitesInboundCallbacks WHERE DedupeHash = @Hash)
BEGIN
    INSERT INTO dbo.FourKitesInboundCallbacks
        (MessageType, FourKitesLoadId, LoadNumber, ReferenceNumbersJson, RawPayload, DedupeHash, ReceivedUtc)
    VALUES
        (@MessageType, @FkLoadId, @LoadNumber, @RefNums, @RawPayload, @Hash, @ReceivedUtc);
    SELECT CAST(1 AS BIT);
END
ELSE
BEGIN
    SELECT CAST(0 AS BIT);
END";
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@MessageType", (object)messageType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FkLoadId", (object)fkLoadId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LoadNumber", (object)loadNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RefNums", (object)referenceNumbersJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RawPayload", rawPayload ?? string.Empty);
                cmd.Parameters.AddWithValue("@Hash", hash);
                cmd.Parameters.AddWithValue("@ReceivedUtc", DateTime.UtcNow);

                await cn.OpenAsync().ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return (bool)result;
            }
        }

        private static string ComputeHash(string messageType, long? fkLoadId, string rawPayload)
        {
            using (var sha = SHA256.Create())
            {
                var input = (messageType ?? "") + "|" + (fkLoadId?.ToString() ?? "") + "|" + (rawPayload ?? "");
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
