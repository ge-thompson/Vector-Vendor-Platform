using System;

namespace Vendor.Common.Persistence
{
    /// <summary>
    /// Strongly-typed representation of one row in VendorAPI_FK.VendorOutboundTransactions.
    /// Used by <see cref="OutboundTransactionRepository"/> to insert new rows and update
    /// existing ones as the dispatch lifecycle progresses.
    ///
    /// Status lifecycle (per Deliverable #7):
    ///   PENDING → ACK → CONFIRMED   (success path)
    ///                 → REJECTED    (webhook reported app-level errors)
    ///   PENDING → HTTP_FAIL          (4xx synchronous)
    ///   PENDING → TRANSPORT_FAIL     (network failure)
    ///   PENDING → RATE_LIMITED       (429; will retry)
    ///   PENDING → SKIPPED            (no profile matched the dispatch)
    ///   PENDING → DEAD_LETTER        (exhausted retries)
    /// </summary>
    public class OutboundTransactionRow
    {
        public long TransactionId { get; set; }

        // Identity / routing
        public string VendorName { get; set; }
        public string EventTypeName { get; set; }
        public string VectorLoadId { get; set; }
        public string ShipperCode { get; set; }
        public string SourceSystem { get; set; }

        // Status
        public string Status { get; set; }              // PENDING, ACK, CONFIRMED, REJECTED, HTTP_FAIL, ...
        public int? HttpStatusCode { get; set; }
        public string ErrorCategory { get; set; }       // Transient, Permanent, RateLimit, Skipped, Unknown
        public string ErrorMessage { get; set; }

        // Vendor correlation
        public string VendorRequestId { get; set; }
        public string VendorLoadId { get; set; }
        public string ExpectedCallbackType { get; set; }

        // Payloads
        public string RequestPayload { get; set; }
        public string ResponseBody { get; set; }

        // Timing
        public DateTime CreatedUtc { get; set; }
        public DateTime? AckUtc { get; set; }
        public DateTime? ConfirmedUtc { get; set; }
        public int? DurationMs { get; set; }
    }
}
