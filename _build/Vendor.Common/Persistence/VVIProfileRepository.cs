using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using Vendor.Common.Configuration;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// Read access to dbo.VVIProfiles. Real-time, read-on-every-call (no cache) so a
    /// profile change takes effect on the next dispatch with no restart — matching the
    /// "transparent and immediate" goal of VVI.
    ///
    /// The core method, GetActiveProfiles(customerId, eventName), runs the dispatch
    /// lookup: all active rows for the customer whose flag for that event is set. Returns
    /// 0..N rows; the dispatcher fans out to each. Zero rows is normal (no routing
    /// configured for that customer/event) — not an error.
    ///
    /// Style follows the rest of Vendor.Common.Persistence: raw ADO.NET, null-tolerant
    /// reads, never throws to the caller (errors surface through onError and yield an
    /// empty list, so a profile-store hiccup can't break the dispatch path).
    /// </summary>
    public class VVIProfileRepository
    {
        private readonly string _connectionString;
        private readonly Action<Exception> _onError;

        public VVIProfileRepository(string connectionString, Action<Exception> onError = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));
            _connectionString = connectionString;
            _onError = onError ?? (_ => { });
        }

        // Maps a VVI event name to its bit column. Whitelist — anything else => no match.
        private static string EventColumn(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return null;
            switch (eventName.Trim().ToLowerInvariant())
            {
                case "loadposted":         return "LoadPosted";
                case "checkcall":          return "CheckCall";
                case "appointmentchanged": return "AppointmentChanged";
                case "pod":                return "POD";
                case "trackingstatus":     return "TrackingStatus";
                case "cancelload":         return "CancelLoad";
                case "invoice":            return "Invoice";
                default:                   return null;
            }
        }

        /// <summary>
        /// Returns all active profiles for the customer that have the given event enabled.
        /// Empty list if none (normal), or on any error (logged via onError). Never throws.
        /// </summary>
        public List<VVIProfile> GetActiveProfiles(int customerId, string eventName)
        {
            var results = new List<VVIProfile>();

            var column = EventColumn(eventName);
            if (column == null)
            {
                _onError(new ArgumentException($"Unknown VVI event name '{eventName}'."));
                return results;
            }

            // Column name comes from the whitelist above, never from user input — safe to inline.
            // Phase B: LEFT JOIN VendorConfigs so we get the authoritative ConfigJson in one query.
            var sql =
                "SELECT vp.ID, vp.CustomerID, vp.Customer, vp.Vendor, vp.AdapterName, vp.Active, " +
                "vp.LoadPosted, vp.CheckCall, vp.AppointmentChanged, vp.POD, vp.TrackingStatus, vp.CancelLoad, vp.Invoice, " +
                "vp.EndpointUrl, vp.AuthType, vp.ApiKey, vp.HeaderName, vp.Username, vp.Password, vp.Secret, " +
                "vp.SignatureHeader, vp.SignatureEncoding, vp.Instructions, vp.Notes, vp.CreatedDate, vp.ModifiedDate, " +
                "vp.VendorConfigID, vc.ConfigJson AS VendorConfigJson " +
                "FROM dbo.VVIProfiles vp " +
                "LEFT JOIN dbo.VendorConfigs vc ON vc.ConfigID = vp.VendorConfigID AND vc.IsActive = 1 " +
                "WHERE vp.CustomerID = @CustomerID AND vp.Active = 1 AND vp." + column + " = 1;";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.AddWithValue("@CustomerID", customerId);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            results.Add(Map(r));
                    }
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                // leave results empty — a profile-store failure must not break dispatch
            }

            return results;
        }

        /// <summary>
        /// Returns all active profiles for a customer regardless of event (e.g. for an
        /// admin/dashboard view). Empty on error. Never throws.
        /// </summary>
        public List<VVIProfile> GetActiveProfilesForCustomer(int customerId)
        {
            var results = new List<VVIProfile>();

            const string sql =
                "SELECT vp.ID, vp.CustomerID, vp.Customer, vp.Vendor, vp.AdapterName, vp.Active, " +
                "vp.LoadPosted, vp.CheckCall, vp.AppointmentChanged, vp.POD, vp.TrackingStatus, vp.CancelLoad, vp.Invoice, " +
                "vp.EndpointUrl, vp.AuthType, vp.ApiKey, vp.HeaderName, vp.Username, vp.Password, vp.Secret, " +
                "vp.SignatureHeader, vp.SignatureEncoding, vp.Instructions, vp.Notes, vp.CreatedDate, vp.ModifiedDate, " +
                "vp.VendorConfigID, vc.ConfigJson AS VendorConfigJson " +
                "FROM dbo.VVIProfiles vp " +
                "LEFT JOIN dbo.VendorConfigs vc ON vc.ConfigID = vp.VendorConfigID AND vc.IsActive = 1 " +
                "WHERE vp.CustomerID = @CustomerID AND vp.Active = 1;";

            try
            {
                using (var cn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand(sql, cn))
                {
                    cmd.Parameters.AddWithValue("@CustomerID", customerId);
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            results.Add(Map(r));
                    }
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
            }

            return results;
        }

        // ─── Row mapping ──────────────────────────────────────────────────
        private static VVIProfile Map(SqlDataReader r)
        {
            return new VVIProfile
            {
                Id                 = GetInt(r, 0),
                CustomerID         = GetInt(r, 1),
                Customer           = GetStr(r, 2),
                Vendor             = GetStr(r, 3),
                AdapterName        = GetStr(r, 4),
                Active             = GetBool(r, 5),
                LoadPosted         = GetBool(r, 6),
                CheckCall          = GetBool(r, 7),
                AppointmentChanged = GetBool(r, 8),
                POD                = GetBool(r, 9),
                TrackingStatus     = GetBool(r, 10),
                CancelLoad         = GetBool(r, 11),
                Invoice            = GetBool(r, 12),
                EndpointUrl        = GetStr(r, 13),
                AuthType           = GetStr(r, 14),
                ApiKey             = GetStr(r, 15),
                HeaderName         = GetStr(r, 16),
                Username           = GetStr(r, 17),
                Password           = GetStr(r, 18),
                Secret             = GetStr(r, 19),
                SignatureHeader    = GetStr(r, 20),
                SignatureEncoding  = GetStr(r, 21),
                Instructions       = GetStr(r, 22),
                Notes              = GetStr(r, 23),
                CreatedDate        = GetDate(r, 24),
                ModifiedDate       = GetDate(r, 25),
                VendorConfigID     = r.IsDBNull(26) ? (int?)null : Convert.ToInt32(r.GetValue(26)),
                VendorConfigJson   = r.IsDBNull(27) ? null : r.GetString(27)
            };
        }

        private static int GetInt(SqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
        private static bool GetBool(SqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
        private static string GetStr(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        private static DateTime GetDate(SqlDataReader r, int i) => r.IsDBNull(i) ? DateTime.MinValue : r.GetDateTime(i);
    }
}
