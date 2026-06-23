using System;
using System.Collections.Generic;
using System.Text;
using Vendor.Common.Configuration;

namespace Vendor.Common.Security
{
    /// <summary>
    /// Vendor-agnostic webhook authentication. Given a <see cref="WebhookAuthConfig"/>,
    /// validates an inbound request's headers (and raw body, for HMAC) against the
    /// configured scheme.
    ///
    /// SCHEMES:
    ///   - "apikey": a configured header must equal a configured value.
    ///   - "basic":  HTTP Basic auth; username + password must match.
    ///   - "hmac":   HMAC-SHA256 of the raw body with a shared secret; digest in a header.
    ///   - "none":   no authentication (protection is at the network layer, e.g. IP allowlist).
    ///
    /// SECURITY: runs INLINE on every webhook request. Comparisons are constant-time to
    /// avoid timing attacks. Never throws — any failure or parse error returns false
    /// (fail-closed).
    ///
    /// A vendor's IWebhookSignatureValidator implementation loads its own
    /// WebhookAuthConfig (from its ConfigJson) and delegates here. The vendor-specific
    /// part is only "where does my config come from"; the validation itself is shared.
    /// </summary>
    public static class WebhookAuthValidator
    {
        /// <summary>
        /// Validates the request against the given auth config. Returns false (fail-closed)
        /// on null config, unknown scheme, or any error. Never throws.
        /// </summary>
        public static bool IsValid(WebhookAuthConfig cfg, IDictionary<string, string> headers, string rawBody)
        {
            try
            {
                if (cfg == null) return false;

                var scheme = (cfg.Scheme ?? "apikey").Trim().ToLowerInvariant();
                switch (scheme)
                {
                    case "apikey":
                        return ValidateApiKey(headers, cfg);
                    case "basic":
                        return ValidateBasicAuth(headers, cfg);
                    case "hmac":
                        return ValidateHmac(headers, rawBody, cfg);
                    case "none":
                        return true;  // protection is at the network layer (IP allowlist)
                    default:
                        return false; // unknown scheme = fail closed
                }
            }
            catch
            {
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

        /// <summary>Case-insensitive header lookup. Defensive even if the caller's
        /// dictionary is already case-insensitive.</summary>
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
        /// could detect prefix matches by measuring response latency.
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
