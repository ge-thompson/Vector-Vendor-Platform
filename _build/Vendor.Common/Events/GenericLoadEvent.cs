using System.Collections.Generic;

namespace Vendor.Common.Events
{
    /// <summary>
    /// Escape hatch for events that don't fit the strongly-typed event classes.
    /// Adapters opt in by checking EventName.
    ///
    /// Prefer the strongly-typed events when one fits. Use GenericLoadEvent only when:
    ///   - The event is one-off (not worth a new event type)
    ///   - The event is rare enough that adapter complexity isn't justified
    ///   - Prototyping a new event before promoting it to a strongly-typed class
    /// </summary>
    public class GenericLoadEvent : VendorEvent
    {
        /// <summary>The event name. Adapters check this in their CanHandle() implementation.</summary>
        public string EventName { get; set; }

        /// <summary>Arbitrary key/value data. Adapters interpret per their EventName handling.</summary>
        public Dictionary<string, object> Data { get; set; }
    }
}
