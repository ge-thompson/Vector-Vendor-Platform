using System;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A status milestone occurred (arrived, departed, exception, etc.).
    /// The framework's enum is intentionally coarse; the raw upstream code is preserved
    /// in SourceStatusCode for adapters that need finer granularity (e.g., a vendor's EDI 214 mapping).
    ///
    /// Source of truth (per Phase 2 dedup policy): split — TruckTools-driven codes from
    /// OTR API; business-driven statuses (rate-confirmed, dispatcher-cancelled, etc.) from FBS.
    /// </summary>
    public class LoadStatusEvent : VendorEvent
    {
        public LoadStatusType StatusType { get; set; }

        /// <summary>When the status milestone occurred (NOT when we received it).</summary>
        public DateTime StatusTimeUtc { get; set; }

        /// <summary>Which stop this status pertains to, if applicable. Optional.</summary>
        public StopInfo AtStop { get; set; }

        /// <summary>
        /// Full stop list for the load, when the vendor being dispatched to requires
        /// the whole itinerary rather than just the changed stop.
        ///
        /// FourKites' Dispatcher API works with a single stop update (populated from
        /// <see cref="AtStop"/>). Project 44's carrier visibility API requires ALL
        /// stops on every appointment update — populate this list from FBS in that
        /// case. Producers that don't have or don't need the full list can leave it
        /// null; adapters that require it will fail cleanly if it's missing.
        ///
        /// When both are populated, adapters that use single-stop APIs prefer
        /// <see cref="AtStop"/> and ignore <see cref="Stops"/>; adapters that use
        /// full-itinerary APIs prefer <see cref="Stops"/> and use <see cref="AtStop"/>
        /// only to identify which stop in the list changed.
        /// </summary>
        public System.Collections.Generic.List<StopInfo> Stops { get; set; }

        /// <summary>
        /// Raw status code from the upstream source (e.g., TruckTools code, FBS internal code).
        /// Preserved verbatim so adapters can translate to finer-grained vendor codes
        /// when StatusType is too coarse.
        /// </summary>
        public string SourceStatusCode { get; set; }

        /// <summary>Human-readable description from the upstream source. Optional.</summary>
        public string SourceStatusDescription { get; set; }
    }
}
