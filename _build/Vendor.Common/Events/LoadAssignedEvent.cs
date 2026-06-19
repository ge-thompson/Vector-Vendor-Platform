using System.Collections.Generic;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A carrier, driver, and/or equipment has been assigned (or reassigned) to a load.
    /// Idempotent across vendors: sending twice with the same driver/equipment is harmless.
    /// Reassignments use the same event type — adapters overwrite the prior assignment.
    ///
    /// Source of truth (per Phase 2 dedup policy): FBS knows about assignment before TruckTools
    /// echoes it. In Phase 1 (FBS-direct not yet active), OTR API fires this on TrackLoad
    /// and UpdateTrackLoad endpoints.
    ///
    /// Carrying as much load context as the source has at assignment time helps vendor
    /// dashboards show a complete picture immediately, without waiting for follow-up calls.
    /// All optional sub-records are null when the source doesn't have the data.
    /// </summary>
    public class LoadAssignedEvent : VendorEvent
    {
        // ─── Required-ish (almost always present) ─────────────────────────────

        public CarrierInfo Carrier { get; set; }
        public DriverInfo Driver { get; set; }
        public EquipmentInfo Equipment { get; set; }

        // ─── Load context (optional but valuable) ─────────────────────────────

        /// <summary>Mode/load type from the source system (e.g., "TL", "LTL"). Free text.</summary>
        public string LoadType { get; set; }

        /// <summary>Trailer type at load-level (mirror of Equipment.TrailerType for convenience). Free text.</summary>
        public string TrailerType { get; set; }

        /// <summary>Free-text notes attached to the load. Optional.</summary>
        public string LoadNotes { get; set; }

        /// <summary>True if the load is being run by a team of drivers. Null if unknown.</summary>
        public bool? IsTeamLoad { get; set; }

        /// <summary>External load identifier from the originating system. Optional.</summary>
        public string ExternalLoadId { get; set; }

        // ─── Contact / origination context ────────────────────────────────────

        /// <summary>Vector dispatcher who owns the load. Optional.</summary>
        public DispatcherInfo Dispatcher { get; set; }

        /// <summary>Originating shipper (customer). Optional.</summary>
        public ShipperInfo Shipper { get; set; }

        // ─── Itinerary ────────────────────────────────────────────────────────

        /// <summary>
        /// Stops on the load's itinerary, in sequence. First stop is the pickup and last
        /// is the delivery in the common single-pickup / single-delivery case; multi-stop
        /// loads use Intermediate roles for stops in between. Null or empty when stops
        /// aren't yet known at assignment time.
        /// </summary>
        public List<StopInfo> Stops { get; set; }
    }
}
