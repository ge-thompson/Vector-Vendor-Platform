using System;

namespace Vendor.Common.Dispatch
{
    /// <summary>
    /// Resolves a VectorLoadId to the ShipperCode that should be used for routing.
    ///
    /// PHASE 1 BEHAVIOR: always returns "VECTOR_DEFAULT" (per open item O-501 — the
    /// Vector Load table location and schema haven't been confirmed, and the Phase 1
    /// catch-all routing row in ClientProfiles uses the VECTOR_DEFAULT shipper code
    /// to match every load).
    ///
    /// PHASE 2+ BEHAVIOR: real lookup against Vector's Load table or FBS. When O-501
    /// is resolved, replace the body of <see cref="Resolve"/> with a SqlCommand against
    /// the actual table. The interface and call sites do not change.
    ///
    /// Designed as a class rather than an interface to keep Phase 1 simple. If
    /// multiple resolution strategies emerge (FBS-driven vs. OTR-driven), promote
    /// to an interface at that time.
    /// </summary>
    public class LoadShipperResolver
    {
        /// <summary>
        /// Phase 1 default shipper code. Documented in Deliverable #10 Section 4.2
        /// as the "floor" that matches every load.
        /// </summary>
        public const string DefaultShipperCode = "VECTOR_DEFAULT";

        private readonly string _connectionString;

        /// <summary>
        /// Constructor takes a connection string for future use (the real lookup
        /// against Vector's Load table). Phase 1 ignores it — but accepting it now
        /// means Phase 2 doesn't need a constructor signature change.
        /// </summary>
        public LoadShipperResolver(string connectionString = null)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Phase 1: returns <see cref="DefaultShipperCode"/> for any input.
        /// Phase 2+: will look up the actual shipper from Vector's Load table.
        ///
        /// Returns DefaultShipperCode (never null) if the load isn't found —
        /// fail-open behavior so events still dispatch to the default routing.
        /// </summary>
        public string Resolve(string vectorLoadId)
        {
            if (string.IsNullOrWhiteSpace(vectorLoadId))
                return DefaultShipperCode;

            // PHASE 1: catch-all. See class XML comments for the Phase 2 upgrade path.
            return DefaultShipperCode;

            // PHASE 2 SHAPE (commented for future reference):
            //
            // using (var cn = new SqlConnection(_connectionString))
            // using (var cmd = new SqlCommand(
            //     "SELECT ShipperCode FROM dbo.[Load] WHERE LoadId = @Id", cn))
            // {
            //     cmd.Parameters.AddWithValue("@Id", vectorLoadId);
            //     cn.Open();
            //     var result = cmd.ExecuteScalar();
            //     return (result == null || result == DBNull.Value)
            //         ? DefaultShipperCode
            //         : (string)result;
            // }
        }
    }
}
