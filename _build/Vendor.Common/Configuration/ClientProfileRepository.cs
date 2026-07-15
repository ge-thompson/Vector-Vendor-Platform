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
        /// LEGACY OVERLOAD — kept for backward compatibility with callers that don't have
        /// a BillToID in scope. Uses ClientProfiles alone (no customer scoping); should
        /// only be used by code paths that predate the VVIProfiles routing.
        ///
        /// Routing semantics:
        /// - Profiles where ShipperCode = shipperCode AND IsActive AND EnabledEvents contains eventType
        /// - PLUS profiles where ShipperCode = "VECTOR_DEFAULT" AND IsActive AND EnabledEvents contains eventType
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

        /// <summary>
        /// Customer-scoped routing driven by VVIProfiles. This is the canonical routing
        /// method — all new dispatch paths should use this.
        ///
        /// Flow:
        /// 1. Query VVIProfiles for rows matching (CustomerID = evt.BillToID) AND Active = 1
        ///    AND the flag column corresponding to evt's type = 1.
        /// 2. For each matched VVIProfile, look up the vendor's ClientProfile row (keyed
        ///    on VendorName = VVIProfile.AdapterName) to get the ConfigJson the adapter
        ///    needs. Vector has ONE integration per vendor — the vendor's connection
        ///    details are shared across customers.
        /// 3. Return the enriched list.
        ///
        /// Events with BillToID = 0 return nothing (prevents unscoped leaks). Events whose
        /// customer has no matching VVIProfile return nothing (clean SKIP in the audit).
        ///
        /// <paramref name="customerHasAnyActiveProfile"/> is set to true when the customer
        /// has AT LEAST ONE active VVIProfile row (regardless of which event flags are on),
        /// and false when the customer isn't set up for VVI at all. The dispatcher uses
        /// this to distinguish "customer isn't a VVI customer" (silent — don't audit) from
        /// "customer is set up but this event isn't enabled" (audit as SKIPPED).
        /// </summary>
        public IReadOnlyList<ClientProfile> FindRoutingByCustomer(
            Vendor.Common.Events.VendorEvent evt,
            out bool customerHasAnyActiveProfile)
        {
            customerHasAnyActiveProfile = false;
            if (evt == null) return new List<ClientProfile>();
            if (evt.BillToID <= 0) return new List<ClientProfile>();

            var flagColumn = MapEventTypeToVVIFlag(evt);
            if (flagColumn == null) return new List<ClientProfile>();  // event type not routed via VVIProfiles

            var vendorConfigs = GetAllProfiles();  // cached ClientProfiles rows keyed by VendorName
            var routingLookup = LoadRoutedAdaptersForCustomer(evt.BillToID, flagColumn);
            customerHasAnyActiveProfile = routingLookup.CustomerHasAnyActiveProfile;

            if (routingLookup.AdapterNames.Count == 0) return new List<ClientProfile>();

            var result = new List<ClientProfile>(routingLookup.AdapterNames.Count);
            foreach (var adapterName in routingLookup.AdapterNames)
            {
                // Vendor config: match ClientProfile.VendorName = VVIProfile.AdapterName.
                // Prefer the row for VECTOR_DEFAULT (Vector uses one FK account across customers)
                // over any shipper-specific override, though today we have only VECTOR_DEFAULT.
                ClientProfile match = null;
                for (int i = 0; i < vendorConfigs.Count; i++)
                {
                    var p = vendorConfigs[i];
                    if (!p.IsActive) continue;
                    if (!string.Equals(p.VendorName, adapterName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (match == null || string.Equals(p.ShipperCode, "VECTOR_DEFAULT", StringComparison.OrdinalIgnoreCase))
                        match = p;
                }

                if (match != null)
                    result.Add(match);
                else
                    _onError(new InvalidOperationException(
                        $"VVIProfile for CustomerID={evt.BillToID} routes to adapter '{adapterName}' but no active ClientProfile row exists for that vendor. Adapter config missing."));
            }

            return result;
        }

        /// <summary>
        /// Backward-compat overload that discards the customer-has-any-active-profile flag.
        /// New callers should prefer the out-parameter overload so they can distinguish
        /// "no VVI customer" from "VVI customer but this event is off".
        /// </summary>
        public IReadOnlyList<ClientProfile> FindRoutingByCustomer(Vendor.Common.Events.VendorEvent evt)
        {
            bool _;
            return FindRoutingByCustomer(evt, out _);
        }

        /// <summary>
        /// Maps a VendorEvent to the VVIProfiles column name that gates its dispatch.
        /// Returns null for event types that don't participate in VVIProfiles routing.
        ///
        /// Special case: LoadStatusEvent branches by SourceStatusDescription — an
        /// "AppointmentChanged" status routes on AppointmentChanged, everything else
        /// (EDI 214 milestones like X3/AF/D1) routes on CheckCall.
        /// </summary>
        private static string MapEventTypeToVVIFlag(Vendor.Common.Events.VendorEvent evt)
        {
            var typeName = evt.GetType().Name;
            switch (typeName)
            {
                case "LoadCreatedEvent":
                case "LoadAssignedEvent":
                    return "LoadPosted";
                case "LocationReportedEvent":
                    // Raw GPS pings / breadcrumb trail — gated by TrackingStatus.
                    // (Vector-authored check calls and location-inferred milestones travel
                    // as LoadStatusEvent and are gated by CheckCall.)
                    return "TrackingStatus";
                case "LoadStatusEvent":
                    {
                        var ls = evt as Vendor.Common.Events.LoadStatusEvent;
                        if (ls != null && string.Equals(ls.SourceStatusDescription, "AppointmentChanged", StringComparison.OrdinalIgnoreCase))
                            return "AppointmentChanged";
                        return "CheckCall";
                    }
                case "LoadTrackingStoppedEvent":
                    return "CancelLoad";
                case "DocumentAvailableEvent":
                    return "POD";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Small tuple-ish struct returned by <see cref="LoadRoutedAdaptersForCustomer"/> so
        /// the caller can distinguish "customer isn't a VVI customer at all" from "customer
        /// is a VVI customer but this event's flag is off." Both cases return zero adapter
        /// names, but only the second one merits an audit row.
        /// </summary>
        private struct VVIRoutingLookup
        {
            public List<string> AdapterNames;
            public bool CustomerHasAnyActiveProfile;
        }

        /// <summary>
        /// Reads VVIProfiles for a specific customer + event flag. Returns the list of
        /// AdapterNames that should receive the event AND a flag indicating whether the
        /// customer has any active VVIProfile at all. Not cached — called on every
        /// dispatch — because VVIProfiles is small (dozens of rows) and edits should
        /// take effect immediately.
        /// </summary>
        private VVIRoutingLookup LoadRoutedAdaptersForCustomer(int customerId, string flagColumn)
        {
            var result = new VVIRoutingLookup
            {
                AdapterNames = new List<string>(),
                CustomerHasAnyActiveProfile = false
            };

            // Whitelist the flag column against known values — defense against injection
            // even though only internal code calls this.
            switch (flagColumn)
            {
                case "LoadPosted":
                case "CheckCall":
                case "AppointmentChanged":
                case "POD":
                case "CancelLoad":
                case "TrackingStatus":
                case "Invoice":
                    break;
                default:
                    _onError(new InvalidOperationException($"Unknown VVIProfiles flag column '{flagColumn}'"));
                    return result;
            }

            // Single query returns every active profile for the customer plus the flag value.
            // If any rows come back, CustomerHasAnyActiveProfile = true. Rows with the flag
            // set to 1 become the adapter list.
            var sql = $@"
SELECT AdapterName, [{flagColumn}] AS FlagOn
FROM dbo.VVIProfiles
WHERE CustomerID = @CustomerID
  AND Active = 1;";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.AddWithValue("@CustomerID", customerId);
                    cn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.CustomerHasAnyActiveProfile = true;
                            if (!reader.IsDBNull(1) && reader.GetBoolean(1))
                            {
                                result.AdapterNames.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                // Fail closed on VVIProfiles read errors — better to skip than to leak.
            }

            return result;
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
