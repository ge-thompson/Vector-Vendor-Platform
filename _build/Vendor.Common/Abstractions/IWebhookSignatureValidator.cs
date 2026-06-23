using System.Collections.Generic;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// Validates the authenticity of an inbound webhook. Implemented per vendor.
    ///
    /// Different vendors authenticate differently:
    /// - Transport-level only: apikey header or HTTP Basic auth on the request,
    ///   with no body signing
    /// - HMAC-SHA256 of the raw body using a shared secret
    /// - JWT in an Authorization header
    ///
    /// The framework treats all of these uniformly via this interface. The controller
    /// calls IsValid before persisting the callback. Validation failure returns 401 to
    /// the vendor; no row is written.
    /// </summary>
    public interface IWebhookSignatureValidator
    {
        /// <summary>The vendor name this validator handles.</summary>
        string VendorName { get; }

        /// <summary>
        /// Returns true if the incoming webhook is authentic.
        ///
        /// Implementations should:
        /// - Read whatever auth signal the vendor uses (header, body field, etc.)
        /// - Compare against credentials loaded from the ClientProfile.ConfigJson
        /// - Return false on any auth failure or parsing error
        /// - Never throw (catch internally and return false)
        ///
        /// The headers dictionary is case-insensitive.
        /// </summary>
        bool IsValid(IDictionary<string, string> headers, string rawBody);
    }
}
