using System;
using System.Collections.Generic;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Security;

namespace Vendor.FourKites.Webhooks
{
    /// <summary>
    /// Validates inbound FourKites webhooks. Implements IWebhookSignatureValidator.
    ///
    /// The only vendor-specific responsibility here is locating this vendor's webhook
    /// auth settings (scanning ClientProfileRepository for an active FourKites profile
    /// and reading webhookAuth from its ConfigJson). The actual scheme validation —
    /// apikey / basic / hmac / none, constant-time compares, HMAC recomputation — is
    /// shared framework logic in Vendor.Common.Security.WebhookAuthValidator.
    ///
    /// CONFIG LOOKUP: for a single active profile this is unambiguous. Inbound webhooks
    /// don't carry a shipper hint in their headers, so multi-tenant inbound routing is a
    /// later concern.
    /// </summary>
    public class FourKitesWebhookSignatureValidator : IWebhookSignatureValidator
    {
        public string VendorName => "FourKites";

        private readonly ClientProfileRepository _profileRepository;
        private readonly Action<Exception> _onError;

        /// <summary>Parameterless constructor — for registry reflection. NOT recommended;
        /// the validator won't have a profile repository and will fail-closed (reject all).</summary>
        public FourKitesWebhookSignatureValidator()
            : this(null, null)
        {
        }

        /// <summary>Registry-friendly constructor matching the DI shape.</summary>
        public FourKitesWebhookSignatureValidator(
            ClientProfileRepository profileRepository,
            Action<Exception> errorHandler)
        {
            _profileRepository = profileRepository;
            _onError = errorHandler ?? (_ => { });
        }

        /// <summary>
        /// Returns true if the inbound request authenticates against this vendor's
        /// configured webhook auth scheme. NEVER THROWS — fail-closed on any error.
        /// </summary>
        public bool IsValid(IDictionary<string, string> headers, string rawBody)
        {
            try
            {
                if (_profileRepository == null) return false;  // no config = fail closed

                var cfg = LoadActiveConfig();
                if (cfg?.WebhookAuth == null) return false;

                return WebhookAuthValidator.IsValid(cfg.WebhookAuth, headers, rawBody);
            }
            catch (Exception ex)
            {
                _onError(ex);
                return false;
            }
        }

        /// <summary>
        /// Loads this vendor's config from the first active matching profile.
        /// </summary>
        private FourKitesConfig LoadActiveConfig()
        {
            var profiles = _profileRepository.GetAllProfiles();
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (!p.IsActive) continue;
                if (!string.Equals(p.VendorName, "FourKites", StringComparison.OrdinalIgnoreCase)) continue;

                try { return FourKitesConfig.ParseFrom(p.ConfigJson); }
                catch { /* try the next profile */ }
            }
            return null;
        }
    }
}
