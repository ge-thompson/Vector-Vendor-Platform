using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// Reads ClientProfile rows from VendorAPI_FK.ClientProfiles.
    ///
    /// CACHING STRATEGY: profiles are read on every dispatch (high-volume hot path).
    /// We cache results in memory with a short TTL (default 60 seconds) so config
    /// changes are picked up reasonably quickly without hammering the database.
    ///
    /// THREAD SAFETY: the cache is read by many concurrent dispatches and refreshed
    /// by one. We use a simple lock around refresh; reads use a snapshot reference
    /// that's safe to read without locking (the reference itself is atomic on .NET).
    ///
    /// FAIL-OPEN BEHAVIOR: if the DB read fails (transient error, DB down), we
    /// return the last good cache snapshot rather than empty. A stale config is
    /// better than no dispatch — operations can fix the DB while loads keep flowing.
    /// The error is logged via the optional error handler so monitoring sees it.
    /// </summary>
    public class ClientProfileRepository
    {
        private readonly string _connectionString;
        private readonly TimeSpan _cacheTtl;
        private readonly Action<Exception> _onError;

        // Cache snapshot. Replaced atomically; readers don't need to lock.
        private volatile CacheSnapshot _snapshot;

        // Serializes refresh attempts so we don't have N threads stampeding the DB
        // when the cache expires under load.
        private readonly object _refreshLock = new object();

        public ClientProfileRepository(
            string connectionString,
            TimeSpan? cacheTtl = null,
            Action<Exception> errorHandler = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            _connectionString = connectionString;
            _cacheTtl = cacheTtl ?? TimeSpan.FromSeconds(60);
            _onError = errorHandler ?? (_ => { });
            _snapshot = new CacheSnapshot(new List<ClientProfile>(), DateTime.MinValue);
        }

        /// <summary>
        /// Returns all ClientProfile rows matching the given (shipper, eventType) routing,
        /// for use by the dispatcher.
        ///
        /// Routing semantics:
        /// - Profiles where ShipperCode = shipperCode AND IsActive AND EnabledEvents contains eventType
        /// - PLUS profiles where ShipperCode = "VECTOR_DEFAULT" AND IsActive AND EnabledEvents contains eventType
        /// - The default is a floor, not a fallback — both match. See Deliverable #10 Section 4.2.
        /// </summary>
        public IReadOnlyList<ClientProfile> FindRouting(string shipperCode, string eventTypeName)
        {
            var all = GetAllProfiles();
            var matched = new List<ClientProfile>();

            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p.IsActive) continue;
                if (!p.IsEventEnabled(eventTypeName)) continue;

                bool shipperMatch =
                    (!string.IsNullOrEmpty(shipperCode)
                        && string.Equals(p.ShipperCode, shipperCode, StringComparison.OrdinalIgnoreCase))
                    || string.Equals(p.ShipperCode, "VECTOR_DEFAULT", StringComparison.OrdinalIgnoreCase);

                if (shipperMatch) matched.Add(p);
            }

            return matched;
        }

        /// <summary>Returns the cached list of profiles, refreshing if the TTL has expired.</summary>
        public IReadOnlyList<ClientProfile> GetAllProfiles()
        {
            var current = _snapshot;
            if (DateTime.UtcNow - current.LoadedUtc < _cacheTtl)
                return current.Profiles;

            // Stale — try to refresh under the lock. Other threads see the old snapshot
            // until the refresh completes.
            lock (_refreshLock)
            {
                // Double-check after acquiring lock — another thread may have just refreshed.
                current = _snapshot;
                if (DateTime.UtcNow - current.LoadedUtc < _cacheTtl)
                    return current.Profiles;

                try
                {
                    var fresh = LoadAllFromDatabase();
                    _snapshot = new CacheSnapshot(fresh, DateTime.UtcNow);
                    return _snapshot.Profiles;
                }
                catch (Exception ex)
                {
                    // Fail open: log and return whatever we had.
                    _onError(ex);
                    // Extend the TTL on the existing snapshot a bit so we don't retry
                    // immediately on every dispatch when the DB is down.
                    _snapshot = new CacheSnapshot(current.Profiles,
                        DateTime.UtcNow - _cacheTtl + TimeSpan.FromSeconds(10));
                    return current.Profiles;
                }
            }
        }

        /// <summary>Force a cache refresh on next read. For testing / admin tools.</summary>
        public void InvalidateCache()
        {
            _snapshot = new CacheSnapshot(_snapshot.Profiles, DateTime.MinValue);
        }

        private List<ClientProfile> LoadAllFromDatabase()
        {
            const string sql = @"
SELECT ProfileId, ShipperCode, VendorName, IsActive, EnabledEvents, ConfigJson,
       Notes, CreatedUtc, UpdatedUtc
FROM dbo.ClientProfiles;";

            var profiles = new List<ClientProfile>();
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cn.Open();
                using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        profiles.Add(new ClientProfile
                        {
                            ProfileId     = reader.GetInt64(0),
                            ShipperCode   = reader.GetString(1),
                            VendorName    = reader.GetString(2),
                            IsActive      = reader.GetBoolean(3),
                            EnabledEvents = reader.GetString(4),
                            ConfigJson    = reader.GetString(5),
                            Notes         = reader.IsDBNull(6) ? null : reader.GetString(6),
                            CreatedUtc    = reader.GetDateTime(7),
                            UpdatedUtc    = reader.GetDateTime(8)
                        });
                    }
                }
            }
            return profiles;
        }

        // ─── Async variant — used by the WebhookCorrelator's background loop ───

        public async Task<IReadOnlyList<ClientProfile>> GetAllProfilesAsync(CancellationToken ct = default)
        {
            // The async path doesn't bother with the cache because the correlator
            // calls this rarely. Direct read keeps the code simple.
            const string sql = @"
SELECT ProfileId, ShipperCode, VendorName, IsActive, EnabledEvents, ConfigJson,
       Notes, CreatedUtc, UpdatedUtc
FROM dbo.ClientProfiles;";

            var profiles = new List<ClientProfile>();
            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                await cn.OpenAsync(ct).ConfigureAwait(false);
                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        profiles.Add(new ClientProfile
                        {
                            ProfileId     = reader.GetInt64(0),
                            ShipperCode   = reader.GetString(1),
                            VendorName    = reader.GetString(2),
                            IsActive      = reader.GetBoolean(3),
                            EnabledEvents = reader.GetString(4),
                            ConfigJson    = reader.GetString(5),
                            Notes         = await reader.IsDBNullAsync(6, ct).ConfigureAwait(false) ? null : reader.GetString(6),
                            CreatedUtc    = reader.GetDateTime(7),
                            UpdatedUtc    = reader.GetDateTime(8)
                        });
                    }
                }
            }
            return profiles;
        }

        // ─── Cache snapshot — immutable, swapped atomically on refresh ─────────

        private sealed class CacheSnapshot
        {
            public IReadOnlyList<ClientProfile> Profiles { get; }
            public DateTime LoadedUtc { get; }

            public CacheSnapshot(IReadOnlyList<ClientProfile> profiles, DateTime loadedUtc)
            {
                Profiles = profiles;
                LoadedUtc = loadedUtc;
            }
        }
    }
}
