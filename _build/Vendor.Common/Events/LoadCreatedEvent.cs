using System.Collections.Generic;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A new load was created in the source system and is now visible/active.
    /// Typically dispatched once per load when it's first booked.
    ///
    /// Source of truth (per Phase 2 dedup policy): OTR API. FBS does not dispatch this
    /// (FBS-originated loads still flow through OTR API which dispatches LoadCreatedEvent).
    /// </summary>
    public class LoadCreatedEvent : VendorEvent
    {
        public StopInfo Origin { get; set; }
        public StopInfo Destination { get; set; }

        /// <summary>All stops including origin and destination, in sequence.</summary>
        public List<StopInfo> Stops { get; set; }

        /// <summary>Transportation mode: "TL", "LTL", "INTERMODAL", etc.</summary>
        public string Mode { get; set; }

        /// <summary>Equipment type: "Dry Van", "Reefer", "Flatbed", etc.</summary>
        public string EquipmentType { get; set; }

        public decimal? Weight { get; set; }

        /// <summary>Weight unit: "LB" or "KG".</summary>
        public string WeightUnit { get; set; }

        /// <summary>Reference numbers for the load (BOL, PO, shipper ref, etc.).</summary>
        public List<ReferenceNumber> References { get; set; }
    }
}
