using System;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// Per-shipper, per-vendor configuration row from VendorAPI_FK.ClientProfiles.
    ///
    /// The framework loads these rows and passes the matching one to the adapter on every
    /// dispatch. Each adapter parses its own <see cref="ConfigJson"/> blob to extract
    /// vendor-specific settings (API key, base URL, etc.).
    ///
    /// Adding a vendor never requires schema changes — only new rows with new ConfigJson shapes.
    /// </summary>
    public class ClientProfile
    {
        /// <summary>Primary key from the ClientProfiles table.</summary>
        public long ProfileId { get; set; }

        /// <summary>
        /// Shipper identifier. "VECTOR_DEFAULT" is the Phase 1 catch-all that matches any load.
        /// Multi-shipper deployments will have specific values per shipper.
        /// </summary>
        public string ShipperCode { get; set; }

        /// <summary>The vendor this profile configures (e.g., "FourKites", "Project44").</summary>
        public string VendorName { get; set; }

        /// <summary>If false, the dispatcher skips this profile entirely. Soft delete.</summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// CSV of event type names this profile dispatches to the vendor (e.g.,
        /// "LoadCreatedEvent,LocationReportedEvent,LoadStatusEvent").
        /// Use <see cref="IsEventEnabled"/> to check membership.
        /// </summary>
        public string EnabledEvents { get; set; }

        /// <summary>
        /// Vendor-specific configuration blob. Each adapter parses this for its own needs:
        /// FK looks for apiKey + billToCode + baseUrl + webhookAuth;
        /// P44 would look for oauthClientId + oauthClientSecret + region.
        /// </summary>
        public string ConfigJson { get; set; }

        /// <summary>Free-text notes for operators. Not used by code.</summary>
        public string Notes { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }

        /// <summary>
        /// True if the given event type name is in this profile's EnabledEvents CSV.
        /// Comparison is case-insensitive and tolerant of whitespace around commas.
        /// </summary>
        public bool IsEventEnabled(string eventTypeName)
        {
            if (string.IsNullOrWhiteSpace(EnabledEvents) || string.IsNullOrWhiteSpace(eventTypeName))
                return false;

            // Fast path: contains check before parsing
            if (EnabledEvents.IndexOf(eventTypeName, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            // Precise check: split and compare each token
            foreach (var raw in EnabledEvents.Split(','))
            {
                if (string.Equals(raw.Trim(), eventTypeName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
