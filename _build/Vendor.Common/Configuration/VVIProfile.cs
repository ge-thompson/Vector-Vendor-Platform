using System;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// One row from dbo.VVIProfiles — a single customer-to-vendor routing instruction.
    ///
    /// Self-contained, modeled on the EDIProfiles pattern: each row carries the customer
    /// it routes for, the vendor it sends to, which lifecycle events are enabled (as flags),
    /// and everything needed to make the call (endpoint + inline credentials).
    ///
    /// A customer (CustomerID = BillToID) may have many rows — one per vendor — and they
    /// all fire independently when an enabled event occurs (the EDI fan-out pattern, e.g.
    /// UNFI dispatching the same event to both FourKites and UNFI).
    ///
    /// AdapterName selects the payload-shaper (Vendor.{AdapterName}Adapter). Vendor is a
    /// cosmetic label only.
    /// </summary>
    public class VVIProfile
    {
        public int Id { get; set; }

        /// <summary>Customer identifier = BillToID. The routing key (with Active) for dispatch.</summary>
        public int CustomerID { get; set; }

        /// <summary>Customer name. Cosmetic.</summary>
        public string Customer { get; set; }

        /// <summary>Vendor label. Cosmetic.</summary>
        public string Vendor { get; set; }

        /// <summary>Selects the payload-shaper / adapter (Vendor.{AdapterName}Adapter).</summary>
        public string AdapterName { get; set; }

        /// <summary>If false, the dispatcher skips this row. Lets inactive test copies coexist.</summary>
        public bool Active { get; set; }

        // ─── Event enable flags (the VVI lifecycle) ──────────────────────
        public bool LoadPosted { get; set; }
        public bool CheckCall { get; set; }
        public bool AppointmentChanged { get; set; }
        public bool POD { get; set; }
        public bool TrackingStatus { get; set; }
        public bool CancelLoad { get; set; }
        public bool Invoice { get; set; }

        // ─── Connection + credentials (inline, EDIProfiles style) ────────
        public string EndpointUrl { get; set; }

        /// <summary>Auth scheme: "apikey", "basic", "hmac", or "none".</summary>
        public string AuthType { get; set; }

        public string ApiKey { get; set; }
        public string HeaderName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Secret { get; set; }
        public string SignatureHeader { get; set; }
        public string SignatureEncoding { get; set; }

        /// <summary>Per-profile non-credential config (JSON). Optional.</summary>
        public string Instructions { get; set; }

        public string Notes { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // ─── Vendor-config link (Phase B) ──────────────────────

        /// <summary>FK to dbo.VendorConfigs. Null on legacy rows; the dispatcher falls
        /// back to synthesizing config from the inline columns above when null.</summary>
        public int? VendorConfigID { get; set; }

        /// <summary>The joined VendorConfigs.ConfigJson, populated when VendorConfigID is
        /// set and the linked VendorConfig row is active. Null otherwise. This is the
        /// authoritative source for adapter config once Phase B is deployed — the inline
        /// EndpointUrl/ApiKey/etc columns are legacy fallback.</summary>
        public string VendorConfigJson { get; set; }

        /// <summary>
        /// True if the named event is enabled on this profile. The dispatcher can use
        /// either this helper or query directly by column. Names match the VVI event types.
        /// </summary>
        public bool IsEventEnabled(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName)) return false;
            switch (eventName.Trim().ToLowerInvariant())
            {
                case "loadposted":         return LoadPosted;
                case "checkcall":          return CheckCall;
                case "appointmentchanged": return AppointmentChanged;
                case "pod":                return POD;
                case "trackingstatus":     return TrackingStatus;
                case "cancelload":         return CancelLoad;
                case "invoice":            return Invoice;
                default:                   return false;
            }
        }

        /// <summary>
        /// Builds a WebhookAuthConfig-shaped view of this profile's inline auth columns,
        /// for reuse of the shared auth logic if ever needed on the outbound side.
        /// </summary>
        public WebhookAuthConfig ToAuthConfig()
        {
            return new WebhookAuthConfig
            {
                Scheme            = string.IsNullOrWhiteSpace(AuthType) ? "none" : AuthType,
                HeaderName        = HeaderName,
                ExpectedValue     = ApiKey,
                BasicUsername     = Username,
                BasicPassword     = Password,
                HmacSecret        = Secret,
                SignatureHeader   = SignatureHeader,
                SignatureEncoding = string.IsNullOrWhiteSpace(SignatureEncoding) ? "hex" : SignatureEncoding
            };
        }
    }
}
