using System;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A status milestone occurred (arrived, departed, exception, etc.).
    /// The framework's enum is intentionally coarse; the raw upstream code is preserved
    /// in SourceStatusCode for adapters that need finer granularity (e.g., FK's EDI 214 mapping).
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
        /// Raw status code from the upstream source (e.g., TruckTools code, FBS internal code).
        /// Preserved verbatim so adapters can translate to finer-grained vendor codes
        /// when StatusType is too coarse.
        /// </summary>
        public string SourceStatusCode { get; set; }

        /// <summary>Human-readable description from the upstream source. Optional.</summary>
        public string SourceStatusDescription { get; set; }
    }
}
