using System;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A GPS position was reported for a load. Highest-volume event type — fires every
    /// ~15 minutes per active load. Adapters MUST respect their vendor's rate limit when
    /// translating these.
    ///
    /// Latitude and Longitude are strings to preserve precision verbatim from upstream
    /// sources (different telematics providers use different formats). Adapters parse
    /// as needed for their vendor's API.
    ///
    /// Source of truth (per Phase 2 dedup policy): OTR API (TruckTools is the only GPS source).
    /// </summary>
    public class LocationReportedEvent : VendorEvent
    {
        public string Latitude { get; set; }
        public string Longitude { get; set; }

        /// <summary>When the position was measured (NOT when we received it).</summary>
        public DateTime LocatedAtUtc { get; set; }

        public string City { get; set; }
        public string State { get; set; }
        public string Country { get; set; }

        /// <summary>Speed in miles per hour, if reported by upstream. Optional.</summary>
        public double? SpeedMph { get; set; }

        /// <summary>Heading in degrees (0-360), if reported. Optional.</summary>
        public double? Heading { get; set; }
    }
}
