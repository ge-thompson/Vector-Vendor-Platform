using System;
using System.Collections.Generic;
using System.Text;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;

namespace Vendor.FourKites.Webhooks
{
    /// <summary>
    /// Validates inbound FourKites webhooks. Implements IWebhookSignatureValidator.
    ///
    /// AUTHENTICATION SCHEMES SUPPORTED (per ConfigJson webhookAuth.scheme):
    ///   - "apikey" (default): FK sends a configured header value; we compare.
    ///   - "basic":  HTTP Basic auth in the Authorization header; we compare username + password.
    ///   - "none":   No authentication. Used only when IP allowlisting is the protection
    ///               mechanism (e.g., FK whitelists Vector's edge IP); validator returns true.
    ///
    /// O-001 STATUS: still open with FK CSM. Default "apikey" because it's the most
    /// common pattern in their integrations. When FK confirms what they actually use,
    /// the change is ONE ConfigJson edit — no code change.
    ///
    /// SECURITY: this validator runs INLINE on every webhook request. It must be fast
    /// (no I/O, no allocations in the comparison path) and constant-time on the
    /// comparison itself to avoid timing attacks.
    ///
    /// CONFIG LOOKUP: the validator finds FK's webhook auth settings by scanning
    /// ClientProfileRepository for any active FK profile and reading webhookAuth from
    /// its ConfigJson. For Phase 1 (one VECTOR_DEFAULT + FourKites profile) this is
    /// unambiguous. Multi-tenant would require routing per-shipper, but FK webhooks
    /// don't carry a shipper hint in their headers, so multi-tenant on inbound is
    /// a Phase 3+ concern.
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
        /// Returns true if the inbound request appears to be from FourKites.
        /// NEVER THROWS — catches all exceptions and returns false (fail-closed).
        /// </summary>
        public bool IsValid(IDictionary<string, string> headers, string rawBody)
        {
            try
            {
                if (_profileRepository == null) return false;  // no config = fail closed

                var cfg = LoadActiveConfig();
                if (cfg?.WebhookAuth == null) return false;

                var scheme = (cfg.WebhookAuth.Scheme ?? "apikey").Trim().ToLowerInvariant();

                switch (scheme)
                {
                    case "apikey":
                        return ValidateApiKey(headers, cfg.WebhookAuth);
                    case "basic":
                        return ValidateBasicAuth(headers, cfg.WebhookAuth);
                    case "hmac":
                        return ValidateHmac(headers, rawBody, cfg.WebhookAuth);
                    case "none":
                        return true;  // protection is at the network layer (IP allowlist)
                    default:
                        return false;  // unknown scheme = fail closed
                }
            }
            catch (Exception ex)
            {
                _onError(ex);
                return false;
            }
        }

        // ─── Per-scheme validators ────────────────────────────────────────

        private static bool ValidateApiKey(IDictionary<string, string> headers, WebhookAuthConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.HeaderName) || string.IsNullOrEmpty(cfg.ExpectedValue))
                return false;

            var actual = TryGetHeader(headers, cfg.HeaderName);
            if (string.IsNullOrEmpty(actual)) return false;

            return ConstantTimeEquals(actual, cfg.ExpectedValue);
        }

        private static bool ValidateBasicAuth(IDictionary<string, string> headers, WebhookAuthConfig cfg)
        {
            if (string.IsNullOrEmpty(cfg.BasicUsername) || string.IsNullOrEmpty(cfg.BasicPassword))
                return false;

            var auth = TryGetHeader(headers, "Authorization");
            if (string.IsNullOrEmpty(auth)) return false;

            // Expected: "Basic <base64(user:pass)>"
            const string prefix = "Basic ";
            if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string decoded;
            try
            {
                var b64 = auth.Substring(prefix.Length).Trim();
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch { return false; }

            var colonIndex = decoded.IndexOf(':');
            if (colonIndex < 0) return false;

            var user = decoded.Substring(0, colonIndex);
            var pass = decoded.Substring(colonIndex + 1);

            return ConstantTimeEquals(user, cfg.BasicUsername)
                && ConstantTimeEquals(pass, cfg.BasicPassword);
        }

        private static bool ValidateHmac(IDictionary<string, string> headers, string rawBody, WebhookAuthConfig cfg)
        {
            if (string.IsNullOrEmpty(cfg.HmacSecret)) return false;
            if (string.IsNullOrWhiteSpace(cfg.SignatureHeader)) return false;
            if (rawBody == null) rawBody = string.Empty;

            var provided = TryGetHeader(headers, cfg.SignatureHeader);
            if (string.IsNullOrEmpty(provided)) return false;

            // Tolerate a leading "sha256=" prefix (GitHub-style).
            const string shaPrefix = "sha256=";
            if (provided.StartsWith(shaPrefix, StringComparison.OrdinalIgnoreCase))
                provided = provided.Substring(shaPrefix.Length);
            provided = provided.Trim();

            string computed;
            try
            {
                byte[] key = Encoding.UTF8.GetBytes(cfg.HmacSecret);
                byte[] body = Encoding.UTF8.GetBytes(rawBody);
                byte[] hash;
                using (var hmac = new System.Security.Cryptography.HMACSHA256(key))
                {
                    hash = hmac.ComputeHash(body);
                }

                var encoding = (cfg.SignatureEncoding ?? "hex").Trim().ToLowerInvariant();
                if (encoding == "base64")
                {
                    computed = Convert.ToBase64String(hash);
                }
                else // "hex" (default)
                {
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        sb.Append(hash[i].ToString("x2"));
                    computed = sb.ToString();
                }
            }
            catch { return false; }

            // hex compares case-insensitively; base64 is case-sensitive.
            var enc = (cfg.SignatureEncoding ?? "hex").Trim().ToLowerInvariant();
            if (enc == "base64")
                return ConstantTimeEquals(provided, computed);
            return ConstantTimeEquals(provided.ToLowerInvariant(), computed.ToLowerInvariant());
        }

        // ─── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Loads the FK webhook config from the first active FK profile.
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

        /// <summary>Case-insensitive header lookup. Dictionary may already be case-insensitive
        /// (the controller should pass one that is), but be defensive in case it isn't.</summary>
        private static string TryGetHeader(IDictionary<string, string> headers, string name)
        {
            if (headers == null) return null;
            if (headers.TryGetValue(name, out var v)) return v;
            foreach (var kv in headers)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// Constant-time string comparison. Prevents timing attacks where an attacker
        /// could detect prefix matches by measuring response latency. Slightly slower
        /// than == but the cost is irrelevant at webhook frequencies.
        /// </summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
