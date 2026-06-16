using System;
using System.Collections.Generic;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// Correlation-friendly metadata extracted from a webhook payload at receipt time
    /// by the vendor's <see cref="IInboundEventProcessor.ParseAndExtract"/>.
    ///
    /// Stored in indexed columns on VendorInboundCallbacks so the background correlator
    /// can match against outbound transactions without re-parsing the raw payload.
    /// </summary>
    public class InboundEventMetadata
    {
        /// <summary>
        /// Vendor-defined event/message type from the payload (e.g., FK's "LOAD_CREATION",
        /// "STOP_ARRIVAL", or any vendor-specific string).
        /// </summary>
        public string MessageType { get; set; }

        /// <summary>
        /// The vendor's internal load identifier from the payload, if present.
        /// For FK: FourKitesLoadId. For project44: their shipment id. Etc.
        /// </summary>
        public string VendorLoadId { get; set; }

        /// <summary>
        /// VectorLoadId discoverable from the payload, if the vendor echoes back our
        /// reference. FK echoes loadNumber which IS our VectorLoadId.
        /// </summary>
        public string VectorLoadId { get; set; }

        /// <summary>
        /// Any reference numbers found in the payload (BOL, PO, etc.). Used as fallback
        /// correlation keys when VectorLoadId and VendorLoadId aren't enough.
        /// </summary>
        public List<string> ReferenceNumbers { get; set; }

        /// <summary>
        /// Did the vendor report success? Distinct from HTTP-level ACK — this is the
        /// application-level outcome. FK puts this in an "IsSuccess" field.
        /// True by default (assume success unless we have evidence otherwise).
        /// </summary>
        public bool IsSuccess { get; set; } = true;

        /// <summary>
        /// If the vendor reported errors, the JSON serialization of those errors.
        /// Stored for forensics; not parsed further by the framework.
        /// </summary>
        public string ErrorsJson { get; set; }
    }

    /// <summary>
    /// Row from VendorAPI_FK.VendorInboundCallbacks. Passed to the inbound processor
    /// during correlation. Field shapes mirror the schema from Deliverable #7.
    /// </summary>
    public class InboundCallbackRow
    {
        public long CallbackId { get; set; }
        public string VendorName { get; set; }
        public string PayloadHash { get; set; }
        public string RawPayload { get; set; }
        public string MessageType { get; set; }
        public string VendorLoadId { get; set; }
        public string VectorLoadId { get; set; }
        public string ReferenceNumbersJson { get; set; }
        public bool? IsSuccess { get; set; }
        public string ErrorsJson { get; set; }
        public DateTime ReceivedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public int ReceiptCount { get; set; }
        public DateTime? ProcessedUtc { get; set; }
        public long? MatchedTransactionId { get; set; }
        public string CorrelationStatus { get; set; }
        public string CorrelationError { get; set; }
    }
}
