namespace Vendor.Common.Configuration
{
    /// <summary>
    /// Webhook authentication settings — how an inbound caller proves its identity.
    /// Vendor-agnostic. A vendor's ConfigJson includes a "webhookAuth" object that
    /// deserializes into this type; the generic <see cref="Vendor.Common.Security.WebhookAuthValidator"/>
    /// interprets it. Adding or changing a vendor's auth method is a ConfigJson edit,
    /// never a code change.
    /// </summary>
    public class WebhookAuthConfig
    {
        /// <summary>Auth scheme: "apikey" (default), "basic", "hmac", or "none".</summary>
        public string Scheme { get; set; } = "apikey";

        // ─── apikey scheme ──────────────────────────────────────────────

        /// <summary>Header name the credential arrives in. For apikey scheme.</summary>
        public string HeaderName { get; set; } = "X-Webhook-Key";

        /// <summary>Expected credential value to match against. For apikey scheme.</summary>
        public string ExpectedValue { get; set; }

        // ─── basic scheme ───────────────────────────────────────────────

        /// <summary>Basic-auth username. Used only when Scheme = "basic".</summary>
        public string BasicUsername { get; set; }

        /// <summary>Basic-auth password. Used only when Scheme = "basic".</summary>
        public string BasicPassword { get; set; }

        // ─── hmac scheme ────────────────────────────────────────────────
        // The caller signs the raw request body with a shared secret (HMAC-SHA256)
        // and sends the digest in a header. We recompute and constant-time compare.

        /// <summary>Shared secret used to compute the HMAC. Used only when Scheme = "hmac".</summary>
        public string HmacSecret { get; set; }

        /// <summary>Header the signature arrives in. Default "X-Signature".</summary>
        public string SignatureHeader { get; set; } = "X-Signature";

        /// <summary>
        /// Encoding of the signature value: "hex" (default) or "base64".
        /// A leading "sha256=" prefix is tolerated on compare.
        /// </summary>
        public string SignatureEncoding { get; set; } = "hex";
    }
}
