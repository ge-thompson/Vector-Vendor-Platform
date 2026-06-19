using System;
using System.Configuration;
using System.Web.Http;
using Vendor.Common.Dispatch;
using Vendor.Common.Persistence;

namespace OTR_API.Controllers
{
    /// <summary>
    /// Diagnostic and administration endpoints for the vendor dispatch framework.
    ///
    /// PHASE 1 NOTE: these endpoints are intentionally unauthenticated so they can
    /// be hit from a browser during local testing. BEFORE going to production:
    ///   - Add [HMACAuthentication] OR a separate admin auth scheme
    ///   - Block external access via firewall / IIS URL rewrite
    ///   - Or move to an internal-only admin sub-application
    /// </summary>
    [RoutePrefix("api/admin")]
    public class AdminController : ApiController
    {
        /// <summary>
        /// GET /api/admin/dispatcher-status
        /// Returns a JSON summary of the dispatcher's current state. Safe to call
        /// repeatedly; no side effects. Useful for confirming Phase 1 wiring at a glance.
        /// </summary>
        [HttpGet]
        [Route("dispatcher-status")]
        public IHttpActionResult GetDispatcherStatus()
        {
            var enabledSetting = ConfigurationManager.AppSettings["VendorDispatch.Enabled"];
            bool storeReady = VendorStatusMappingStore.IsInitialized;

            return Ok(new
            {
                VendorDispatcherConfigured = VendorDispatcher.IsConfigured,
                VendorStatusMappingStoreInitialized = storeReady,
                CachedMappingRowCount = storeReady ? VendorStatusMappingStore.Instance.CachedRowCount : 0,
                DispatchEnabledSetting = enabledSetting ?? "(not set; defaults to true)",
                ConnectionStringConfigured = !string.IsNullOrWhiteSpace(
                    ConfigurationManager.AppSettings["VendorDispatch.AuditConnectionString"]),
                ServerTimeUtc = DateTime.UtcNow,
                ReadinessHint = BuildReadinessHint(
                    VendorDispatcher.IsConfigured,
                    storeReady,
                    enabledSetting),
                Notes = "Diagnostic endpoint. Add authentication before exposing to non-internal networks."
            });
        }

        /// <summary>
        /// POST /api/admin/refresh-mappings
        /// Triggers a re-read of the dbo.VendorStatusMapping table into the in-memory
        /// cache. Call this after editing rows in the table so changes take effect
        /// without restarting the application.
        /// </summary>
        [HttpPost]
        [Route("refresh-mappings")]
        public IHttpActionResult RefreshMappings()
        {
            if (!VendorDispatcher.IsConfigured)
                return BadRequest("VendorDispatcher is not configured. Restart the application or check startup errors.");

            try
            {
                VendorDispatcher.Instance.RefreshStatusMappings();

                int rowsAfter = VendorStatusMappingStore.IsInitialized
                    ? VendorStatusMappingStore.Instance.CachedRowCount
                    : 0;

                return Ok(new
                {
                    Success = true,
                    CachedRowCountAfterRefresh = rowsAfter,
                    ServerTimeUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                // Log via DataAudit so it lands in the same place as dispatch errors.
                var da = new OTR_API.DataClasses.DataAudit();
                da.InsertErrorAuditLog(ex.Message, "AdminController.RefreshMappings");
                return InternalServerError(ex);
            }
        }

        private static string BuildReadinessHint(bool configured, bool storeReady, string enabledSetting)
        {
            if (!configured)
                return "NOT READY: VendorDispatcher.Configure() did not run successfully at startup. Check Application_Start logs.";
            if (!storeReady)
                return "PARTIAL: dispatcher is configured but mapping store failed to initialize. Mappers will use hardcoded templates only.";

            bool enabled = !string.Equals(enabledSetting, "false", StringComparison.OrdinalIgnoreCase);
            return enabled
                ? "READY: dispatcher will fire events to configured vendors."
                : "INERT: dispatcher is initialized but VendorDispatch.Enabled=false. Calls will return early without dispatching.";
        }
    }
}
