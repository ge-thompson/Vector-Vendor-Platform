using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// The contract every vendor implementation must fulfill to plug into the framework.
    /// One adapter per vendor. The framework discovers adapters via the &lt;vendorAdapters&gt;
    /// config section and routes events to them based on ClientProfile rows.
    ///
    /// THREE RULES every implementation MUST follow:
    ///
    /// 1. NEVER THROW out of DispatchAsync. Catch internally; return
    ///    VendorOperationResult.Failed(...). The dispatcher relies on this so a misbehaving
    ///    adapter doesn't break dispatch for other vendors in a fan-out scenario.
    ///
    /// 2. SELF-RATE-LIMIT. Each vendor has its own rate limit; the adapter enforces it.
    ///    The framework does NOT coordinate rate limits across vendors.
    ///
    /// 3. POPULATE AUDIT FIELDS. RequestPayloadJson and ResponseBodyJson on the result
    ///    must be set whenever they exist. These are the forensic trail for "what did
    ///    we actually send and what came back?".
    /// </summary>
    public interface IVendorAdapter
    {
        /// <summary>
        /// The vendor name this adapter handles (e.g., "ExampleVendor").
        /// Must match the VendorName column on ClientProfiles and the vendorName attribute
        /// in the &lt;vendorAdapters&gt; config section.
        /// </summary>
        string VendorName { get; }

        /// <summary>
        /// True if this adapter can process the given event type. Adapters may decline
        /// events they don't support (e.g., a vendor that doesn't accept documents
        /// declines DocumentAvailableEvent). The dispatcher logs the skip and continues
        /// to the next vendor in any fan-out.
        ///
        /// Must be fast (no I/O) — called on every dispatch.
        /// </summary>
        bool CanHandle(VendorEvent evt);

        /// <summary>
        /// Translate the event into a vendor-specific payload and send it.
        ///
        /// CONTRACT:
        /// - Must NEVER throw. Catch all exceptions internally and return
        ///   <see cref="VendorOperationResult.Failed(System.Exception, string)"/>.
        /// - Must respect the vendor's rate limit (return Failed with ErrorCategory="RateLimit"
        ///   if locally throttled).
        /// - Must populate RequestPayloadJson and (if available) ResponseBodyJson on the result.
        /// - Should respect the cancellation token for graceful shutdown.
        /// </summary>
        Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt,
            ClientProfile profile,
            CancellationToken cancellationToken = default);
    }
}
