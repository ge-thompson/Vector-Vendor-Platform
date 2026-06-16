using System;
using System.Configuration;
using System.ServiceProcess;
using System.Web.Http;
using Microsoft.Owin.Hosting;
using Owin;

namespace FourKitesIntegration.OutboundService
{
    /// <summary>
    /// Entry point — runs as Windows Service in production, or as a console app when launched interactively.
    /// To install:
    ///     sc create FourKitesOutbound binPath= "C:\Path\To\FourKitesIntegration.OutboundService.exe"
    ///     sc start FourKitesOutbound
    /// To debug locally: just run from Visual Studio; it detects interactive mode and uses console.
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (Environment.UserInteractive)
            {
                using (var svc = new OutboundWindowsService())
                {
                    svc.StartInteractive();
                    Console.WriteLine("FourKites Outbound Service running interactively. Press Enter to exit.");
                    Console.ReadLine();
                    svc.StopInteractive();
                }
            }
            else
            {
                ServiceBase.Run(new ServiceBase[] { new OutboundWindowsService() });
            }
        }
    }

    public class OutboundWindowsService : ServiceBase
    {
        private IDisposable _webApp;

        public OutboundWindowsService()
        {
            ServiceName = "FourKitesOutbound";
        }

        public void StartInteractive() => OnStart(new string[0]);
        public void StopInteractive() => OnStop();

        protected override void OnStart(string[] args)
        {
            var url = ConfigurationManager.AppSettings["InternalListenUrl"];
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("InternalListenUrl is not configured.");

            _webApp = WebApp.Start<OwinStartup>(url);
            EventLog.WriteEntry(ServiceName, "FourKites Outbound Service listening on " + url, System.Diagnostics.EventLogEntryType.Information);
        }

        protected override void OnStop()
        {
            _webApp?.Dispose();
            _webApp = null;
        }
    }

    /// <summary>OWIN startup — wires Web API into the self-host pipeline.</summary>
    public class OwinStartup
    {
        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Formatters.Clear();
            config.Formatters.Add(new System.Net.Http.Formatting.JsonMediaTypeFormatter
            {
                SerializerSettings = Core.Client.FourKitesJson.Settings
            });
            appBuilder.Use<InternalAuthMiddleware>();
            appBuilder.UseWebApi(config);
        }
    }
}
