using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Vendor.Common.Dispatch.Smoke
{
    /// <summary>Shared connection-string + cleanup helpers for dispatch smoke tests.</summary>
    internal static class DbHelper
    {
        public const string TestLoadIdPrefix = "DISPATCH_SMOKE_";

        public static string ConnectionString
            => ConfigurationManager.AppSettings["VendorDispatch.AuditConnectionString"]
               ?? throw new InvalidOperationException(
                   "App.config missing VendorDispatch.AuditConnectionString.");

        /// <summary>Unique test load id per run — keeps reruns from interfering.</summary>
        public static string NewTestLoadId(string suffix)
            => $"{TestLoadIdPrefix}{DateTime.UtcNow:yyyyMMddHHmmss}_{suffix}";

        public static async Task VerifyConnectivityAsync()
        {
            using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new SqlCommand("SELECT DB_NAME();", cn))
                {
                    var dbName = (string)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    if (!string.Equals(dbName, "VendorAPI_FK", StringComparison.OrdinalIgnoreCase))
                        throw new Exception(
                            $"Connected to wrong database: {dbName}. Check connection string.");
                }
            }
        }

        /// <summary>Removes all rows created by DISPATCH_SMOKE_* test runs.</summary>
        public static async Task CleanupTestRowsAsync()
        {
            using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new SqlCommand(
                    "DELETE FROM dbo.VendorOutboundTransactions WHERE VectorLoadId LIKE @P;", cn))
                {
                    cmd.Parameters.AddWithValue("@P", TestLoadIdPrefix + "%");
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Reads back one column from VendorOutboundTransactions by TransactionId.</summary>
        public static async Task<T> ReadOutboundAsync<T>(long transactionId, string columnName)
        {
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(
                $"SELECT {columnName} FROM dbo.VendorOutboundTransactions WHERE TransactionId = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", transactionId);
                await cn.OpenAsync().ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return CoerceValue<T>(result);
            }
        }

        /// <summary>Counts outbound rows for a given load ID.</summary>
        public static async Task<int> CountOutboundRowsAsync(string vectorLoadId)
        {
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.VendorOutboundTransactions WHERE VectorLoadId = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", vectorLoadId);
                await cn.OpenAsync().ConfigureAwait(false);
                return (int)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Returns the first outbound row's TransactionId for a load id, or 0 if none.</summary>
        public static async Task<long> GetSingleTransactionIdAsync(string vectorLoadId)
        {
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(
                "SELECT TOP 1 TransactionId FROM dbo.VendorOutboundTransactions " +
                "WHERE VectorLoadId = @Id ORDER BY CreatedUtc DESC;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", vectorLoadId);
                await cn.OpenAsync().ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return (result == null || result == DBNull.Value) ? 0 : Convert.ToInt64(result);
            }
        }

        /// <summary>
        /// Coerces a scalar from SqlClient into a typed value. Handles DBNull,
        /// nullable target types, and object targets. (Same fix as persistence smoke
        /// helper — Convert.ChangeType can't convert TO Nullable&lt;U&gt; directly.)
        /// </summary>
        private static T CoerceValue<T>(object raw)
        {
            if (raw == null || raw == DBNull.Value) return default;
            if (typeof(T) == typeof(object)) return (T)raw;
            if (raw is T direct) return direct;

            var underlying = Nullable.GetUnderlyingType(typeof(T));
            if (underlying != null)
                return (T)Convert.ChangeType(raw, underlying);

            return (T)Convert.ChangeType(raw, typeof(T));
        }
    }
}
