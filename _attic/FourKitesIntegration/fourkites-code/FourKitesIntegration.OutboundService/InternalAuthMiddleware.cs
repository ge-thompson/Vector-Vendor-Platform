using System;
using System.Configuration;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace FourKitesIntegration.OutboundService
{
    /// <summary>
    /// Tiny middleware that requires X-Internal-Auth header to match the configured token.
    /// This is a private internal API — not exposed to the internet, but defense-in-depth
    /// against accidental loopback abuse from co-hosted processes.
    /// </summary>
    public class InternalAuthMiddleware : OwinMiddleware
    {
        private static readonly string ExpectedToken =
            ConfigurationManager.AppSettings["InternalAuthToken"];

        public InternalAuthMiddleware(OwinMiddleware next) : base(next) { }

        public override async Task Invoke(IOwinContext context)
        {
            // Skip auth for health check
            if (context.Request.Path.Value?.Equals("/health", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Next.Invoke(context).ConfigureAwait(false);
                return;
            }

            var provided = context.Request.Headers["X-Internal-Auth"];
            if (string.IsNullOrEmpty(ExpectedToken) || ExpectedToken == "CHANGE_ME_TO_RANDOM_VALUE")
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal auth token not configured.").ConfigureAwait(false);
                return;
            }
            if (provided != ExpectedToken)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized.").ConfigureAwait(false);
                return;
            }

            await Next.Invoke(context).ConfigureAwait(false);
        }
    }
}
