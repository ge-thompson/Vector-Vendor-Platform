using System;
using System.Configuration;
using System.ServiceProcess;
using System.Web.Http;
using FourKitesIntegration.Core.Client;
using Microsoft.Owin.Hosting;
using Owin;

namespace FourKitesIntegration.WebhookReceiver
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                using (var svc = new WebhookWindowsService())
                {
                    svc.StartInteractive();
                    Console.WriteLine("FourKites Webhook Receiver running interactively. Press Enter to exit.");
                    Console.ReadLine();
                    svc.StopInteractive();
                }
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new WebhookWindowsService() });
            }
        }
    }

    public class WebhookWindowsService : ServiceBase
    {
        private IDisposable _webApp;
        private WebhookCorrelator _correlator;

        public WebhookWindowsService()
        {
            ServiceName = "FourKitesWebhookReceiver";
        }

        public void StartInteractive() => OnStart(new string[0]);
        public void StopInteractive() => OnStop();

        protected override void OnStart(string[] args)
        {
            var url = ConfigurationManager.AppSettings["PublicListenUrl"];
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("PublicListenUrl is not configured.");

            _webApp = WebApp.Start<OwinStartup>(url);
            EventLog.WriteEntry(ServiceName, "Webhook Receiver listening on " + url,
                System.Diagnostics.EventLogEntryType.Information);

            // Start the background correlator. It scans FourKitesInboundCallbacks for unprocessed
            // rows and updates the matching outbound transactions. Disabled if no connection string.
            var cs = ConfigurationManager.AppSettings["ConnectionString"];
            if (!string.IsNullOrEmpty(cs))
            {
                _correlator = new WebhookCorrelator(cs);
                _correlator.Start();
                EventLog.WriteEntry(ServiceName, "Webhook correlator started.",
                    System.Diagnostics.EventLogEntryType.Information);
            }
            else
            {
                EventLog.WriteEntry(ServiceName, "ConnectionString missing — correlator NOT started.",
                    System.Diagnostics.EventLogEntryType.Warning);
            }
        }

        protected override void OnStop()
        {
            _correlator?.Stop();
            _correlator?.Dispose();
            _correlator = null;

            _webApp?.Dispose();
            _webApp = null;
        }
    }

    public class OwinStartup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Formatters.Clear();
            config.Formatters.Add(new System.Net.Http.Formatting.JsonMediaTypeFormatter
            {
                SerializerSettings = FourKitesJson.Settings
            });
            app.Use<WebhookAuthMiddleware>();
            app.UseWebApi(config);
        }
    }
}
