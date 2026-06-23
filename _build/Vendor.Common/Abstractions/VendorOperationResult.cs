using System;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// The result of an adapter dispatching one event to its vendor.
    /// Adapters return one of these from <see cref="IVendorAdapter.DispatchAsync"/>.
    ///
    /// IMPORTANT: adapters MUST NOT throw out of DispatchAsync. Use the static factory
    /// methods on this class (<see cref="Succeeded"/>, <see cref="Failed"/>, etc.) to
    /// build a result for every code path.
    ///
    /// The dispatcher writes the contents of this object directly to
    /// VendorAPI_FK.VendorOutboundTransactions, so every field matters for audit.
    /// </summary>
    public class VendorOperationResult
    {
        /// <summary>True if the vendor accepted the dispatch (HTTP 2xx).</summary>
        public bool Success { get; set; }

        /// <summary>HTTP status code returned by the vendor, if applicable.</summary>
        public int? HttpStatusCode { get; set; }

        /// <summary>
        /// The vendor's own identifier for this request (e.g., a vendor's requestId GUID
        /// or tracking id). Used by support teams to correlate with vendor logs.
        /// </summary>
        public string VendorRequestId { get; set; }

        /// <summary>
        /// The vendor's internal load identifier, if the response included one.
        /// For some vendors, this is the vendor's load id returned on load-creation confirmations.
        /// Stamped on the outbound transaction so future webhooks can correlate.
        /// </summary>
        public string VendorLoadId { get; set; }

        /// <summary>The JSON payload sent to the vendor. Persisted for audit/replay.</summary>
        public string RequestPayloadJson { get; set; }

        /// <summary>The response body received from the vendor (success body or error body).</summary>
        public string ResponseBodyJson { get; set; }

        /// <summary>Human-readable error description if Success is false.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Classification of any error. Allowed values:
        /// "Transient" (retryable network/timeout),
        /// "Permanent" (bad payload, won't succeed on retry),
        /// "RateLimit" (vendor returned 429 or local rate limiter blocked),
        /// "Skipped" (adapter declined to process this event type),
        /// "Unknown" (defensive default).
        /// </summary>
        public string ErrorCategory { get; set; }

        /// <summary>How long the HTTP call took. Useful for performance monitoring.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// If the adapter wants to indicate the outbound transaction should be left in
        /// PENDING/ACK state awaiting a confirming webhook, set this to the expected
        /// callback type (e.g., "LOAD_CREATION"). Null means "no further callback expected".
        /// </summary>
        public string ExpectedCallbackType { get; set; }

        // ─── Factory methods — adapters should use these instead of direct construction ────

        public static VendorOperationResult Succeeded(
            int httpStatusCode,
            string vendorRequestId = null,
            string vendorLoadId = null,
            string requestPayloadJson = null,
            string responseBodyJson = null,
            string expectedCallbackType = null,
            TimeSpan duration = default)
        {
            return new VendorOperationResult
            {
                Success = true,
                HttpStatusCode = httpStatusCode,
                VendorRequestId = vendorRequestId,
                VendorLoadId = vendorLoadId,
                RequestPayloadJson = requestPayloadJson,
                ResponseBodyJson = responseBodyJson,
                ExpectedCallbackType = expectedCallbackType,
                Duration = duration,
                ErrorCategory = null
            };
        }

        public static VendorOperationResult Failed(
            string errorMessage,
            string errorCategory = "Unknown",
            int? httpStatusCode = null,
            string requestPayloadJson = null,
            string responseBodyJson = null,
            TimeSpan duration = default)
        {
            return new VendorOperationResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorCategory = errorCategory,
                HttpStatusCode = httpStatusCode,
                RequestPayloadJson = requestPayloadJson,
                ResponseBodyJson = responseBodyJson,
                Duration = duration
            };
        }

        public static VendorOperationResult Failed(Exception ex, string errorCategory = "Unknown")
        {
            return new VendorOperationResult
            {
                Success = false,
                ErrorMessage = ex?.ToString(),
                ErrorCategory = errorCategory
            };
        }

        public static VendorOperationResult Skipped(string reason)
        {
            return new VendorOperationResult
            {
                Success = false,
                ErrorMessage = reason,
                ErrorCategory = "Skipped"
            };
        }

        public static VendorOperationResult RateLimited(string reason = "Vendor rate limit reached")
        {
            return new VendorOperationResult
            {
                Success = false,
                ErrorMessage = reason,
                ErrorCategory = "RateLimit"
            };
        }
    }
}
