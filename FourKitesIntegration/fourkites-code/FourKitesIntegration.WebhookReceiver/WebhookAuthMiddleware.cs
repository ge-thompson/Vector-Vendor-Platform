using System;
using System.Configuration;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace FourKitesIntegration.WebhookReceiver
{
    /// <summary>
    /// Authenticates inbound webhook callbacks from FourKites.
    /// Supports Basic, custom header (ApiKey), or None modes — set in App.config.
    /// IMPORTANT: combine with IP whitelisting at the firewall — auth is transport-level only,
    /// and FourKites does NOT sign callback bodies.
    /// </summary>
    public class WebhookAuthMiddleware : OwinMiddleware
    {
        private static readonly string Mode =
            ConfigurationManager.AppSettings["WebhookAuthMode"] ?? "None";
        private static readonly string HeaderName =
            ConfigurationManager.AppSettings["WebhookAuthHeader"] ?? "X-FourKites-Token";
        private static readonly string HeaderValue =
            ConfigurationManager.AppSettings["WebhookAuthValue"];
        private static readonly string BasicUser =
            ConfigurationManager.AppSettings["WebhookBasicUser"];
        private static readonly string BasicPassword =
            ConfigurationManager.AppSettings["WebhookBasicPassword"];

        public WebhookAuthMiddleware(OwinMiddleware next) : base(next) { }

        public override async Task Invoke(IOwinContext context)
        {
            if (context.Request.Path.Value?.Equals("/health", StringComparison.OrdinalIgnoreCase) == true)
            {
                await Next.Invoke(context).ConfigureAwait(false);
                return;
            }

            bool authenticated;
            switch (Mode.ToLowerInvariant())
            {
                case "apikey":
                    authenticated = string.Equals(context.Request.Headers[HeaderName], HeaderValue, StringComparison.Ordinal);
                    break;
                case "basic":
                    authenticated = CheckBasic(context.Request.Headers["Authorization"]);
                    break;
                case "none":
                    // Use only for local dev or behind strict IP whitelist.
                    authenticated = true;
                    break;
                default:
                    authenticated = false;
                    break;
            }

            if (!authenticated)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized.").ConfigureAwait(false);
                return;
            }

            await Next.Invoke(context).ConfigureAwait(false);
        }

        private static bool CheckBasic(string authHeader)
        {
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Substring(6)));
                var parts = decoded.Split(new[] { ':' }, 2);
                return parts.Length == 2 &&
                       string.Equals(parts[0], BasicUser, StringComparison.Ordinal) &&
                       string.Equals(parts[1], BasicPassword, StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }
}
