using System;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Configuration;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// Optional capability interface for vendors that support reading current load state
    /// (in addition to receiving event dispatches). Adapters opt in by implementing this
    /// alongside IVendorAdapter.
    ///
    /// The framework treats reads as diagnostic/informational — they are not part of the
    /// event dispatch flow and don't write audit rows by default. Use cases:
    ///   - Verify a load exists in the vendor's system (debugging the silent-no-op case)
    ///   - Inspect stop identifiers / appointment times the vendor currently has on file
    ///   - Future fetch-merge-PATCH: pull current state to base a partial update on
    ///
    /// CONTRACT: implementations MUST NOT throw out of GetLoadAsync. All failures —
    /// network, auth, 4xx/5xx, malformed config — are returned in the result.
    /// </summary>
    public interface IVendorLoadReader
    {
        /// <summary>
        /// Fetch the vendor's current view of a load identified by Vector's load id.
        /// Returns the vendor's raw response body (typically JSON) on success.
        /// </summary>
        Task<VendorLoadReadResult> GetLoadAsync(
            string vectorLoadId,
            ClientProfile profile,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Typed result of a vendor load read. Carries the vendor's raw response body so the
    /// caller (typically a diagnostic endpoint) can return it unmodified, plus HTTP/error
    /// detail for non-success outcomes.
    /// </summary>
    public class VendorLoadReadResult
    {
        public bool Success { get; set; }
        public int? HttpStatusCode { get; set; }
        public string ResponseBody { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorCategory { get; set; }   // "Permanent" | "Transient" | "RateLimit" | "Unknown"
        public TimeSpan Duration { get; set; }

        public static VendorLoadReadResult Ok(int httpStatusCode, string body, TimeSpan duration)
            => new VendorLoadReadResult
            {
                Success = true,
                HttpStatusCode = httpStatusCode,
                ResponseBody = body,
                Duration = duration
            };

        public static VendorLoadReadResult Failed(
            string message, string category,
            int? httpStatusCode = null, string body = null, TimeSpan duration = default(TimeSpan))
            => new VendorLoadReadResult
            {
                Success = false,
                ErrorMessage = message,
                ErrorCategory = category,
                HttpStatusCode = httpStatusCode,
                ResponseBody = body,
                Duration = duration
            };
    }
}
