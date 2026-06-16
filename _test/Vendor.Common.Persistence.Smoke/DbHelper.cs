using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Vendor.Common.Persistence.Smoke
{
    /// <summary>Shared connection-string + cleanup helpers for the persistence smoke tests.</summary>
    internal static class DbHelper
    {
        public const string TestLoadIdPrefix = "SMOKE_";

        public static string ConnectionString
            => ConfigurationManager.AppSettings["VendorAPI_FK.ConnectionString"]
               ?? throw new InvalidOperationException(
                   "App.config missing VendorAPI_FK.ConnectionString. Check the test app's App.config.");

        /// <summary>Unique test load id per run — keeps reruns from interfering.</summary>
        public static string NewTestLoadId(string suffix)
            => $"{TestLoadIdPrefix}{DateTime.UtcNow:yyyyMMddHHmmss}_{suffix}";

        /// <summary>Quick sanity check: can we even open the connection? Run first.</summary>
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

        /// <summary>Removes all rows created by SMOKE_* test load IDs. Safe to run anytime.</summary>
        public static async Task CleanupTestRowsAsync()
        {
            using (var cn = new SqlConnection(ConnectionString))
            {
                await cn.OpenAsync().ConfigureAwait(false);

                // Clean up in dependency order
                using (var cmd = new SqlCommand(
                    "DELETE FROM dbo.LoadCrossReference WHERE VectorLoadId LIKE @P;", cn))
                {
                    cmd.Parameters.AddWithValue("@P", TestLoadIdPrefix + "%");
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                using (var cmd = new SqlCommand(
                    "DELETE FROM dbo.VendorInboundCallbacks WHERE VectorLoadId LIKE @P OR VendorName = 'VERIFY_TEST';", cn))
                {
                    cmd.Parameters.AddWithValue("@P", TestLoadIdPrefix + "%");
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

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

        /// <summary>Reads back one column from VendorInboundCallbacks by CallbackId.</summary>
        public static async Task<T> ReadInboundAsync<T>(long callbackId, string columnName)
        {
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(
                $"SELECT {columnName} FROM dbo.VendorInboundCallbacks WHERE CallbackId = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", callbackId);
                await cn.OpenAsync().ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return CoerceValue<T>(result);
            }
        }

        /// <summary>
        /// Coerces an arbitrary scalar from SqlClient into a typed value, handling:
        ///   - DBNull / null → default(T)
        ///   - Nullable&lt;U&gt; targets (Convert.ChangeType can't handle these directly)
        ///   - object targets (skip conversion to support null-checks like AssertNull)
        ///   - Exact-type matches (skip conversion)
        /// </summary>
        private static T CoerceValue<T>(object raw)
        {
            if (raw == null || raw == DBNull.Value) return default;

            // If the caller asked for 'object', return as-is so AssertNull/AssertNotNull work cleanly
            if (typeof(T) == typeof(object)) return (T)raw;

            // If the raw value is already the target type, no conversion needed
            if (raw is T direct) return direct;

            // Handle Nullable<U> by converting to U then boxing into the nullable wrapper
            var underlying = Nullable.GetUnderlyingType(typeof(T));
            if (underlying != null)
            {
                var converted = Convert.ChangeType(raw, underlying);
                return (T)converted;
            }

            return (T)Convert.ChangeType(raw, typeof(T));
        }

        /// <summary>Helper for assertions: counts SMOKE_ rows in a table by load id.</summary>
        public static async Task<int> CountRowsAsync(string tableName, string vectorLoadId)
        {
            using (var cn = new SqlConnection(ConnectionString))
            using (var cmd = new SqlCommand(
                $"SELECT COUNT(*) FROM dbo.{tableName} WHERE VectorLoadId = @Id;", cn))
            {
                cmd.Parameters.AddWithValue("@Id", vectorLoadId);
                await cn.OpenAsync().ConfigureAwait(false);
                return (int)await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
        }
    }
}
