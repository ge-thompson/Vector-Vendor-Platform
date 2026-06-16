namespace Vendor.Common.Events
{
    /// <summary>
    /// Tracking has been stopped on a load. May indicate cancellation, completion,
    /// or operator-initiated stop.
    ///
    /// The Reason field is free-text by design — common values are "CANCELLED",
    /// "DELIVERED", "DISPATCHER_STOPPED", but adapters should not depend on specific
    /// strings. The framework doesn't enforce an enum here because reason semantics
    /// vary by upstream source.
    ///
    /// Source of truth (per Phase 2 dedup policy): either OTR API or FBS — last-write-wins.
    /// Adapters should handle the small duplication risk gracefully.
    /// </summary>
    public class LoadTrackingStoppedEvent : VendorEvent
    {
        /// <summary>Free-text reason. Common values: "CANCELLED", "DELIVERED", "DISPATCHER_STOPPED".</summary>
        public string Reason { get; set; }
    }
}
