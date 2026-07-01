using System;

namespace Vendor.Common.Events
{
    /// <summary>
    /// Base class for all vendor-agnostic events flowing through VendorDispatcher.
    ///
    /// Three rules every event obeys:
    ///   1. Past tense — events describe what HAPPENED, not what should happen
    ///   2. Always carries VectorLoadId (universal correlation key per D-008)
    ///   3. Carries only what every plausible vendor would need
    ///
    /// Adapters translate these into vendor-specific payloads. Callers never construct
    /// vendor-specific objects.
    /// </summary>
    public abstract class VendorEvent
    {
        /// <summary>
        /// Vector's internal identifier for the load this event pertains to.
        /// The universal correlation key (D-008) — every event has one.
        /// </summary>
        public string VectorLoadId { get; set; }

        /// <summary>
        /// The customer's / shipper's own load or shipment number (sourced from
        /// LoadHeader.MasterBOL in FBS). This is the identifier the shipper and downstream
        /// vendor recognize the load by — distinct from VectorLoadId. Carried on every event
        /// so adapters can key the load on the customer's number where the vendor expects it.
        /// May be empty if not provided.
        /// </summary>
        public string ShipmentNumber { get; set; }

        /// <summary>
        /// UTC timestamp of when the event occurred. Defaults to dispatch time.
        /// Callers may set this explicitly if they're dispatching a historical event.
        /// </summary>
        public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Identifier of the caller that produced this event.
        /// Common values: "OTR_API", "VectorFBS", "POD_App".
        /// Stamped on every audit row so we can answer "where did this come from?".
        /// </summary>
        public string SourceSystem { get; set; }
    }
}
