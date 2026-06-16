namespace Vendor.Common.Events
{
    /// <summary>
    /// A carrier, driver, and/or equipment has been assigned (or reassigned) to a load.
    /// Idempotent across vendors: sending twice with the same driver/equipment is harmless.
    /// Reassignments use the same event type — adapters overwrite the prior assignment.
    ///
    /// Source of truth (per Phase 2 dedup policy): FBS knows about assignment before TruckTools
    /// echoes it. In Phase 1 (FBS-direct not yet active), OTR API dispatches this on TrackLoad
    /// and UpdateTrackLoad endpoints.
    /// </summary>
    public class LoadAssignedEvent : VendorEvent
    {
        public CarrierInfo Carrier { get; set; }
        public DriverInfo Driver { get; set; }
        public EquipmentInfo Equipment { get; set; }
    }
}
