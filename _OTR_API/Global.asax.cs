using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace OTR_API
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        //protected void Application_Start()
        //{
        //    GlobalConfiguration.Configure(WebApiConfig.Register);
        //}

        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            // ─── Vendor dispatch framework ──────────────────────────
            // Initializes the vendor adapter registry from Web.config and
            // configures the dispatcher singleton. Stays inert until the
            // VendorDispatch.Enabled appSetting is flipped to "true".
            try
            {
                Vendor.Common.Dispatch.VendorDispatcher.Configure(
                    errorHandler: ex =>
                    {
                        // Route dispatch errors to existing OTR API audit log
                        try
                        {
                            new OTR_API.DataClasses.DataAudit()
                                .InsertErrorAuditLog(ex.ToString(), "VendorDispatch");
                        }
                        catch { /* never let logging break the app */ }
                    });
            }
            catch (Exception vdEx)
            {
                // Fail loudly at startup if config is bad — never silently disable.
                // This will prevent the app from starting if VendorDispatcher
                // can't initialize, so config issues are caught immediately
                // rather than at first dispatch attempt.
                System.Diagnostics.EventLog.WriteEntry(
                    "Application",
                    "VendorDispatch initialization failed: " + vdEx.ToString(),
                    System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
            // ────────────────────────────────────────────────────────
        }
    }
}
