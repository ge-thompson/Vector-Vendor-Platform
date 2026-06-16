using System.Collections.Generic;
using Vendor.Common.Events;

namespace Vendor.FourKites.Mapping
{
    /// <summary>
    /// Translates the framework's coarse <see cref="LoadStatusType"/> enum into the
    /// granular EDI 214 status codes FourKites consumes.
    ///
    /// THIS TABLE IS THE PHASE 1 BEST-GUESS (open item O-002 in the master README).
    /// It's based on the standard EDI 214 code set documented by ANSI X12. The
    /// canonical mapping per LoadStatusType is what FK most commonly expects, but
    /// some shippers have negotiated alternate codes — refine this table after first
    /// production data shows what FK actually accepts/rejects.
    ///
    /// HOW TO REFINE:
    ///   1. Wait for 100+ outbound transactions with real FK data
    ///   2. Query VendorOutboundTransactions for HTTP_FAIL rows where ResponseBody
    ///      mentions "invalid status code"
    ///   3. Adjust this dictionary based on what FK actually accepts
    ///
    /// HOW TO EXTEND for new event types: see <see cref="MapFromSourceCode"/> —
    /// adapter can also pass the raw upstream code (from TruckTools, etc.) for
    /// finer-grained translation when the coarse enum isn't enough.
    /// </summary>
    public static class LoadStatusMapper
    {
        /// <summary>
        /// Canonical FK EDI 214 code per LoadStatusType. The adapter falls back to
        /// the raw SourceStatusCode (TT code, etc.) when the type-based mapping is
        /// "X9" (catch-all "Other").
        /// </summary>
        private static readonly IReadOnlyDictionary<LoadStatusType, string> CanonicalCodes =
            new Dictionary<LoadStatusType, string>
            {
                // X1 = Arrived at Pickup Location
                { LoadStatusType.ArrivedAtPickup,    "X1" },

                // AF = Carrier Departed Pickup Location with Shipment
                { LoadStatusType.DepartedPickup,     "AF" },

                // X3 = Arrived at Delivery Location
                { LoadStatusType.ArrivedAtDelivery,  "X3" },

                // CD = Carrier Departed Delivery Location
                { LoadStatusType.DepartedDelivery,   "CD" },

                // D1 = Completed Loading (used loosely here as "Delivered" terminal state;
                // some shippers prefer J1. Refine after first prod data.)
                { LoadStatusType.Delivered,          "D1" },

                // AG = Estimated Delivery (used here for the in-transit progression
                // status FK shows on its dashboards)
                { LoadStatusType.InTransit,          "AG" },

                // OA = Dispatched
                { LoadStatusType.Dispatched,         "OA" },

                // A3 = Shipment Returned to Shipper (used as our generic "Exception"
                // signal pending refinement; FK accepts more granular codes via
                // SourceStatusCode pass-through)
                { LoadStatusType.Exception,          "A3" },

                // X9 = catch-all "Other"; the adapter consults SourceStatusCode in this case
                { LoadStatusType.Other,              "X9" }
            };

        /// <summary>
        /// Returns the canonical FK EDI 214 code for the given LoadStatusType.
        /// Returns "X9" (Other) for unknown values — adapter then falls back to SourceStatusCode.
        /// </summary>
        public static string MapStatusType(LoadStatusType statusType)
        {
            return CanonicalCodes.TryGetValue(statusType, out var code) ? code : "X9";
        }

        /// <summary>
        /// Returns the best EDI 214 code for the event:
        ///   1. If StatusType maps to a non-catch-all, use that
        ///   2. Otherwise, if SourceStatusCode is set, pass it through verbatim
        ///      (assumes upstream already gave us an EDI 214 code or near-equivalent)
        ///   3. Otherwise, return "X9"
        ///
        /// The pass-through behavior matters for TruckTools-driven events where
        /// the TT code is already finer-grained than our enum — passing it through
        /// preserves information FK can use.
        /// </summary>
        public static string MapFromEvent(LoadStatusEvent evt)
        {
            if (evt == null) return "X9";

            var canonical = MapStatusType(evt.StatusType);
            if (canonical != "X9") return canonical;

            return string.IsNullOrWhiteSpace(evt.SourceStatusCode) ? "X9" : evt.SourceStatusCode;
        }

        /// <summary>
        /// Returns the canonical codes dictionary for diagnostic/admin tooling.
        /// Exposed so a future "show me the mapping table" admin page works without
        /// reflection.
        /// </summary>
        public static IReadOnlyDictionary<LoadStatusType, string> GetCanonicalCodes()
            => CanonicalCodes;
    }
}
