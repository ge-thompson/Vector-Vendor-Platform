using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;
using Vendor.Common.Security;

namespace Vendor.Project44
{
    public class Project44Adapter : IVendorAdapter, IDisposable
    {
        public string VendorName => "Project44";

        private readonly HttpClient _httpClient;
        private readonly Action<Exception> _onError;

        private OAuth2ClientCredentialsProvider _tokenProvider;
        private string _tokenProviderKey;
        private readonly object _tokenProviderLock = new object();

        public Project44Adapter() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, null) { }

        public Project44Adapter(ClientProfileRepository profileRepository, Action<Exception> errorHandler)
            : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, errorHandler) { }

        public Project44Adapter(HttpClient httpClient, Action<Exception> errorHandler = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _onError = errorHandler ?? (_ => { });
        }

        public bool CanHandle(VendorEvent evt)
        {
            if (evt == null) return false;
            return evt is LoadCreatedEvent
                || evt is LoadAssignedEvent
                || evt is LocationReportedEvent
                || evt is LoadStatusEvent
                || evt is LoadTrackingStoppedEvent;
        }

        public async Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt, ClientProfile profile, CancellationToken cancellationToken = default)
        {
            if (evt == null) return VendorOperationResult.Failed("Event was null", "Unknown");
            if (profile == null) return VendorOperationResult.Failed("ClientProfile was null", "Unknown");

            Project44Config cfg;
            try { cfg = Project44Config.ParseFrom(profile.ConfigJson); }
            catch (Project44ConfigException ex)
            {
                _onError(ex);
                return VendorOperationResult.Failed(
                    "Bad ConfigJson for Project44 profile: " + ex.Message, "Permanent");
            }

            EnsureTokenProvider(cfg);

            try
            {
                switch (evt)
                {
                    case LoadStatusEvent status:
                        return await DispatchStatusAsync(status, cfg, cancellationToken).ConfigureAwait(false);

                    default:
                        return VendorOperationResult.Skipped(
                            $"P44 adapter: {evt.GetType().Name} handler not yet implemented.");
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                return VendorOperationResult.Failed("P44 unhandled error: " + ex.Message, "Permanent");
            }
        }

        private async Task<VendorOperationResult> DispatchStatusAsync(
            LoadStatusEvent evt, Project44Config cfg, CancellationToken ct)
        {
            bool isAppointmentChange = evt != null
                && string.Equals(evt.SourceStatusDescription, "AppointmentChanged", StringComparison.OrdinalIgnoreCase);

            if (isAppointmentChange)
                return await DispatchAppointmentAsync(evt, cfg, ct).ConfigureAwait(false);

            return VendorOperationResult.Skipped(
                "P44 adapter: milestone eventUpdate not yet implemented.");
        }

        private async Task<VendorOperationResult> DispatchAppointmentAsync(
            LoadStatusEvent evt, Project44Config cfg, CancellationToken ct)
        {
            var build = Mapping.PayloadBuilder.BuildAppointmentUpdate(evt, cfg);
            if (!build.IsReady)
                return VendorOperationResult.Skipped(build.SkipReason);

            var client = new Project44Client(_httpClient, _tokenProvider, cfg.BaseUrl);
            var result = await client.PostJsonAsync(cfg.StatusUpdatesEndpoint, build.Json, ct).ConfigureAwait(false);

            if (result.Success)
            {
                return VendorOperationResult.Succeeded(
                    httpStatusCode: result.HttpStatusCode ?? 200,
                    requestPayloadJson: result.RequestPayloadJson,
                    responseBodyJson: result.ResponseBodyJson);
            }

            return VendorOperationResult.Failed(
                errorMessage: result.ErrorMessage ?? "P44 appointment update failed.",
                errorCategory: ClassifyFailure(result.HttpStatusCode),
                httpStatusCode: result.HttpStatusCode,
                requestPayloadJson: result.RequestPayloadJson,
                responseBodyJson: result.ResponseBodyJson);
        }

        private static string ClassifyFailure(int? httpStatusCode)
        {
            if (!httpStatusCode.HasValue) return "Transient";
            int code = httpStatusCode.Value;
            if (code == 429) return "Transient";
            if (code >= 400 && code < 500) return "Permanent";
            if (code >= 500) return "Transient";
            return "Permanent";
        }

        private void EnsureTokenProvider(Project44Config cfg)
        {
            var key = cfg.OauthTokenEndpoint + "|" + cfg.ClientId;
            if (_tokenProviderKey == key && _tokenProvider != null) return;

            lock (_tokenProviderLock)
            {
                if (_tokenProviderKey == key && _tokenProvider != null) return;

                _tokenProvider = new OAuth2ClientCredentialsProvider(
                    tokenEndpoint: cfg.OauthTokenEndpoint,
                    clientId: cfg.ClientId,
                    clientSecret: cfg.ClientSecret,
                    scope: cfg.Scope,
                    httpClient: _httpClient,
                    errorHandler: _onError);
                _tokenProviderKey = key;
            }
        }

        public void Dispose() => _httpClient?.Dispose();
    }
}