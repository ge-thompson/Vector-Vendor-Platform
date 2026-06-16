namespace Vendor.Common.Events
{
    /// <summary>
    /// Vendor-agnostic load status types. Maps to vendor-specific codes inside each adapter.
    /// For TruckTools-driven flows, OTR API translates TT codes to one of these values
    /// and passes the original TT code in <see cref="LoadStatusEvent.SourceStatusCode"/> for
    /// adapters that need finer granularity.
    /// </summary>
    public enum LoadStatusType
    {
        /// <summary>Load has been assigned and dispatched to a carrier/driver.</summary>
        Dispatched,

        /// <summary>Truck has arrived at the pickup location.</summary>
        ArrivedAtPickup,

        /// <summary>Truck has departed the pickup location with the load.</summary>
        DepartedPickup,

        /// <summary>Load is in transit between stops.</summary>
        InTransit,

        /// <summary>Truck has arrived at the delivery location.</summary>
        ArrivedAtDelivery,

        /// <summary>Truck has departed the delivery location (delivery complete).</summary>
        DepartedDelivery,

        /// <summary>Final delivery confirmed.</summary>
        Delivered,

        /// <summary>An exception event occurred (delay, damage, etc.). Consult SourceStatusCode for specifics.</summary>
        Exception,

        /// <summary>Genuinely doesn't fit any other type. Adapters fall back to SourceStatusCode.</summary>
        Other
    }

    /// <summary>
    /// Types of documents that can flow through the framework via DocumentAvailableEvent.
    /// </summary>
    public enum DocumentType
    {
        /// <summary>Proof of Delivery — typically a signed delivery receipt.</summary>
        ProofOfDelivery,

        /// <summary>Bill of Lading.</summary>
        BillOfLading,

        /// <summary>Rate Confirmation.</summary>
        RateConfirmation,

        /// <summary>Weigh slip / scale ticket.</summary>
        WeighSlip,

        /// <summary>Doesn't fit other types.</summary>
        Other
    }

    /// <summary>
    /// Role of a stop in a load's itinerary.
    /// </summary>
    public enum StopRole
    {
        Pickup,
        Delivery,
        Intermediate
    }
}
