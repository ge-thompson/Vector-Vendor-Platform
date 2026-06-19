using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// In-memory cache of dbo.VendorStatusMapping rows, with manual refresh.
    /// Singleton — initialized once at startup by VendorDispatcher.Configure(),
    /// then read by adapter mappers (outbound) and caller mappers (inbound).
    ///
    /// LIFECYCLE:
    ///   1. VendorDispatcher.Configure() calls Initialize() once at app startup.
    ///   2. Mapper code calls GetOutbound() / GetInbound() on every translation.
    ///      Lookup is O(1) on a Dictionary; no DB hit per call.
    ///   3. Admin tooling calls Refresh() when a row is edited to flush the cache.
    ///
    /// FALLBACK PHILOSOPHY: this store returns null when no row matches.
    /// Callers MUST have a hardcoded template they fall back to. DB rows are
    /// OVERRIDES; missing rows mean "use the code's default".
    ///
    /// THREAD SAFETY: Refresh() swaps the entire dictionary atomically. Readers
    /// see either the old or new dictionary, never a half-populated one.
    /// Connection failures during refresh leave the existing cache in place
    /// (rather than blanking it).
    /// </summary>
    public sealed class VendorStatusMappingStore
    {
        // ─── Singleton ────────────────────────────────────────────────────────

        private static VendorStatusMappingStore _instance;

        /// <summary>The initialized singleton. Throws if Initialize() hasn't run.</summary>
        public static VendorStatusMappingStore Instance =>
            _instance ?? throw new InvalidOperationException(
                "VendorStatusMappingStore not initialized. Call VendorStatusMappingStore.Initialize() first " +
                "(normally done by VendorDispatcher.Configure() at app startup).");

        /// <summary>True if Initialize() has been called.</summary>
        public static bool IsInitialized => _instance != null;

        /// <summary>
        /// Initializes the singleton, loads cache from DB, and returns.
        /// Safe to call once; subsequent calls overwrite (use Refresh() for routine reloads).
        /// </summary>
        public static void Initialize(string connectionString, Action<Exception> onError = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            var store = new VendorStatusMappingStore(connectionString, onError);
            store.Refresh();
            _instance = store;
        }

        // ─── Instance ─────────────────────────────────────────────────────────

        private readonly string _connectionString;
        private readonly Action<Exception> _onError;
        private volatile Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private VendorStatusMappingStore(string connectionString, Action<Exception> onError)
        {
            _connectionString = connectionString;
            _onError = onError ?? (_ => { });
        }

        /// <summary>
        /// Reloads the cache from dbo.VendorStatusMapping. If the DB is unreachable,
        /// the existing cache is preserved (rather than blanked) and the error is
        /// surfaced through onError.
        /// </summary>
        public void Refresh()
        {
            const string sql = @"
SELECT VendorName, Direction, SourceSystem, SourceCode, TargetCode
FROM dbo.VendorStatusMapping
WHERE IsActive = 1;";

            var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var vendor = reader.GetString(0);
                            var direction = reader.GetString(1);
                            var sourceSystem = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            var sourceCode = reader.GetString(3);
                            var targetCode = reader.GetString(4);

                            var key = BuildKey(vendor, direction, sourceSystem, sourceCode);
                            next[key] = targetCode;
                        }
                    }
                }

                // Atomic swap — readers see old OR new dict, never partial.
                _cache = next;
            }
            catch (Exception ex)
            {
                _onError(ex);
                // Intentionally leave _cache pointing at the old dictionary.
            }
        }

        /// <summary>
        /// Returns the count of currently-cached rows. Useful for diagnostics.
        /// </summary>
        public int CachedRowCount => _cache.Count;

        // ─── Lookup ───────────────────────────────────────────────────────────

        /// <summary>
        /// Outbound lookup: framework status -> vendor-specific code.
        /// Returns null if no row exists (caller falls back to hardcoded template).
        ///
        /// Example: GetOutbound("FourKites", "ArrivedAtPickup") -> "X1"
        /// </summary>
        public string GetOutbound(string vendorName, string sourceCode)
        {
            if (string.IsNullOrEmpty(vendorName) || string.IsNullOrEmpty(sourceCode))
                return null;

            var key = BuildKey(vendorName, "Outbound", "", sourceCode);
            return _cache.TryGetValue(key, out var target) ? target : null;
        }

        /// <summary>
        /// Inbound lookup: upstream-system code -> framework LoadStatusType (as string).
        /// Returns null if no row exists (caller falls back to hardcoded template).
        ///
        /// Example: GetInbound("FourKites", "TruckTools", "X1") -> "ArrivedAtPickup"
        /// Also accepts vendorName "GLOBAL" for cross-vendor inbound mappings.
        /// </summary>
        public string GetInbound(string vendorName, string sourceSystem, string sourceCode)
        {
            if (string.IsNullOrEmpty(vendorName) || string.IsNullOrEmpty(sourceSystem) || string.IsNullOrEmpty(sourceCode))
                return null;

            // Try vendor-specific first
            var vendorKey = BuildKey(vendorName, "Inbound", sourceSystem, sourceCode);
            if (_cache.TryGetValue(vendorKey, out var target))
                return target;

            // Fall back to GLOBAL inbound mapping
            var globalKey = BuildKey("GLOBAL", "Inbound", sourceSystem, sourceCode);
            return _cache.TryGetValue(globalKey, out target) ? target : null;
        }

        // ─── Key formatting ───────────────────────────────────────────────────

        private static string BuildKey(string vendor, string direction, string sourceSystem, string sourceCode)
        {
            // Pipe-delimited, case-insensitive dictionary handles casing of any component.
            return string.Concat(vendor, "|", direction, "|", sourceSystem ?? "", "|", sourceCode);
        }
    }
}
