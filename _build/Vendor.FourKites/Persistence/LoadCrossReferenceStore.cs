using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Vendor.FourKites.Persistence
{
    /// <summary>
    /// Cross-reference store for resolving VectorLoadId -> FK loadId (and the reverse).
    ///
    /// Why this exists:
    ///   FK Load Create returns a numeric loadId (a.k.a. trackingId) that we MUST
    ///   capture and use on subsequent Update (PATCH /api/v1/tracking/{loadId}) and
    ///   Delete (POST /api/v1/tracking/delete_loads with trackingIds[]) calls. The
    ///   loadNumber we send on Create is NOT a valid identifier for Update/Delete.
    ///
    ///   Without this store, the second LoadAssignedEvent for a load would either:
    ///     - Create a duplicate load in FK (if we POST again), or
    ///     - 404 (if we PATCH but don't know FK's loadId).
    ///
    /// Storage: dbo.LoadCrossReference in VendorAPI_FK. Existing schema (script 04)
    /// already supports this — we just populate VendorLoadId after a successful Create.
    ///
    /// Idempotency:
    ///   - Lookups are pure reads.
    ///   - Persist is "insert or update" via MERGE — calling it with the same FK loadId
    ///     for the same VectorLoadId is a no-op.
    ///
    /// Thread safety: SqlConnection is created per call (cheap with connection pooling).
    /// </summary>
    public class LoadCrossReferenceStore
    {
        private readonly string _connectionString;
        private const string VENDOR_NAME = "FourKites";

        public LoadCrossReferenceStore(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required", nameof(connectionString));
            _connectionString = connectionString;
        }

        /// <summary>
        /// Look up FK's loadId for a given VectorLoadId. Returns null if no row exists.
        /// </summary>
        public async Task<long?> GetFkLoadIdAsync(string vectorLoadId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(vectorLoadId)) return null;

            const string sql = @"
                SELECT TOP 1 VendorLoadId
                FROM dbo.LoadCrossReference
                WHERE VectorLoadId = @VectorLoadId
                  AND VendorName = @VendorName
                  AND TrackingStatus <> 'STOPPED'
                ORDER BY UpdatedUtc DESC;";

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@VectorLoadId", vectorLoadId);
                cmd.Parameters.AddWithValue("@VendorName",   VENDOR_NAME);

                await cn.OpenAsync(cancellationToken).ConfigureAwait(false);
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                if (result == null || result == DBNull.Value) return null;
                if (long.TryParse(result.ToString(), out var id)) return id;
                return null;
            }
        }

        /// <summary>
        /// Persist (or update) the FK loadId for a VectorLoadId after a successful Create.
        /// Idempotent — calling repeatedly with the same values is a no-op.
        /// </summary>
        public async Task PersistAsync(
            string vectorLoadId,
            long fkLoadId,
            string trackingStatus = "ACTIVE",
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(vectorLoadId))
                throw new ArgumentException("VectorLoadId is required", nameof(vectorLoadId));
            if (fkLoadId <= 0)
                throw new ArgumentException("FK loadId must be positive", nameof(fkLoadId));

            const string sql = @"
                MERGE dbo.LoadCrossReference AS tgt
                USING (SELECT @VectorLoadId AS VectorLoadId, @VendorName AS VendorName) AS src
                   ON tgt.VectorLoadId = src.VectorLoadId AND tgt.VendorName = src.VendorName
                WHEN MATCHED THEN
                    UPDATE SET VendorLoadId   = @VendorLoadId,
                               TrackingStatus = @TrackingStatus
                WHEN NOT MATCHED THEN
                    INSERT (VectorLoadId, VendorName, VendorLoadId, TrackingStatus)
                    VALUES (@VectorLoadId, @VendorName, @VendorLoadId, @TrackingStatus);";

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@VectorLoadId",   vectorLoadId);
                cmd.Parameters.AddWithValue("@VendorName",     VENDOR_NAME);
                cmd.Parameters.AddWithValue("@VendorLoadId",   fkLoadId.ToString());
                cmd.Parameters.AddWithValue("@TrackingStatus", (object)trackingStatus ?? DBNull.Value);

                await cn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Mark a cross-reference as STOPPED after a successful Delete.
        /// Keeps the row for audit; future lookups will treat it as "no active FK load."
        /// </summary>
        public async Task MarkStoppedAsync(string vectorLoadId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(vectorLoadId)) return;

            const string sql = @"
                UPDATE dbo.LoadCrossReference
                SET TrackingStatus = 'STOPPED'
                WHERE VectorLoadId = @VectorLoadId
                  AND VendorName = @VendorName;";

            using (var cn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cmd.Parameters.AddWithValue("@VectorLoadId", vectorLoadId);
                cmd.Parameters.AddWithValue("@VendorName",   VENDOR_NAME);

                await cn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
