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
        /// Customer-scoped routing driven by VVIProfiles → VendorConfigs. Canonical routing
        /// method — all new dispatch paths should use this.
        ///
        /// Flow (Phase B onward):
        /// 1. Query VVIProfiles JOIN VendorConfigs for rows matching (CustomerID = evt.BillToID)
        ///    AND VVIProfiles.Active = 1 AND VendorConfigs.IsActive = 1 AND the flag column
        ///    corresponding to evt's type = 1. One row per matched vendor for this customer,
        ///    each row already carries the AdapterName + ConfigJson the adapter needs.
        /// 2. Materialize each row into a ClientProfile (in-memory DTO, not a DB row).
        /// 3. Return the list.
        ///
        /// ClientProfiles is no longer consulted for routing. VVIProfiles + VendorConfigs
        /// is the complete answer to "who receives this event, and how do we reach them?"
        ///
        /// Events with BillToID = 0 return nothing (prevents unscoped leaks). Events whose
        /// customer has no matching VVIProfile return nothing (clean SKIP in the audit).
        /// If a VVIProfile has NULL VendorConfigID, that adapter is skipped and the
        /// misconfiguration is surfaced via <see cref="_onError"/>.
        ///
        /// <paramref name="customerHasAnyActiveProfile"/> is set to true when the customer
        /// has AT LEAST ONE active VVIProfile row (regardless of which event flags are on).
        /// The dispatcher uses this to distinguish "customer isn't a VVI customer" (silent —
        /// don't audit) from "customer is set up but this event isn't enabled" (audit SKIPPED).
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

            var routingLookup = LoadRoutedAdaptersForCustomer(evt.BillToID, flagColumn);
            customerHasAnyActiveProfile = routingLookup.CustomerHasAnyActiveProfile;

            if (routingLookup.Matches.Count == 0) return new List<ClientProfile>();

            // Materialize each (AdapterName, ConfigJson) pair into a ClientProfile.
            // ClientProfile is now purely an in-memory shape the adapter consumes; nothing
            // in this list is loaded from the ClientProfiles table.
            var result = new List<ClientProfile>(routingLookup.Matches.Count);
            foreach (var match in routingLookup.Matches)
            {
                if (string.IsNullOrEmpty(match.ConfigJson))
                {
                    _onError(new InvalidOperationException(
                        $"VVIProfile for CustomerID={evt.BillToID}, AdapterName='{match.AdapterName}' " +
                        "has no VendorConfig attached (VendorConfigID is NULL or the linked config is inactive/missing). Adapter skipped."));
                    continue;
                }

                result.Add(new ClientProfile
                {
                    ProfileId     = 0,                    // synthetic — not from ClientProfiles anymore
                    ShipperCode   = "VECTOR_SHARED",       // vestigial — keeps IsEventEnabled shape happy
                    VendorName    = match.AdapterName,    // AdapterName is what the registry keys on
                    IsActive      = true,                 // already filtered by SQL
                    EnabledEvents = "*",                  // routing decision is already made — all events accepted
                    ConfigJson    = match.ConfigJson,
                    Notes         = null,
                    CreatedUtc    = DateTime.UtcNow,
                    UpdatedUtc    = DateTime.UtcNow
                });
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
        /// is a VVI customer but this event's flag is off." Both cases return zero matches,
        /// but only the second one merits an audit row.
        /// </summary>
        private struct VVIRoutingLookup
        {
            public List<VVIRoutingMatch> Matches;
            public bool CustomerHasAnyActiveProfile;
        }

        /// <summary>One matched VVIProfile row, already joined to its VendorConfig.</summary>
        private struct VVIRoutingMatch
        {
            public string AdapterName;
            public string ConfigJson;
        }

        /// <summary>
        /// Reads VVIProfiles for a specific customer + event flag, joining to VendorConfigs
        /// so the ConfigJson comes back in the same query. Returns the list of matched
        /// (AdapterName, ConfigJson) pairs AND a flag indicating whether the customer has
        /// any active VVIProfile at all.
        ///
        /// Not cached — called on every dispatch — because VVIProfiles + VendorConfigs are
        /// small (dozens of rows) and edits should take effect immediately without a cache
        /// TTL. If DB volume ever justifies caching, add it here.
        ///
        /// The JOIN is LEFT so a VVIProfile with no VendorConfig still surfaces the
        /// customer as "has an active profile" (so we don't drop them to silent-skip land)
        /// but its ConfigJson will be null and <see cref="FindRoutingByCustomer"/> will
        /// skip that adapter with an error via <see cref="_onError"/>.
        /// </summary>
        private VVIRoutingLookup LoadRoutedAdaptersForCustomer(int customerId, string flagColumn)
        {
            var result = new VVIRoutingLookup
            {
                Matches = new List<VVIRoutingMatch>(),
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

            // Single query returns every active profile for the customer, its flag value,
            // and the joined VendorConfig's ConfigJson. Rows with the flag set to 1 AND
            // an active VendorConfig become the match list.
            var sql = $@"
SELECT vp.AdapterName,
       vp.[{flagColumn}] AS FlagOn,
       vc.ConfigJson
FROM dbo.VVIProfiles vp
LEFT JOIN dbo.VendorConfigs vc
       ON vc.ConfigID = vp.VendorConfigID
      AND vc.IsActive = 1
WHERE vp.CustomerID = @CustomerID
  AND vp.Active = 1;";

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
                                result.Matches.Add(new VVIRoutingMatch
                                {
                                    AdapterName = reader.GetString(0),
                                    ConfigJson  = reader.IsDBNull(2) ? null : reader.GetString(2)
                                });
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
