using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Vendor.Common.Abstractions
{
    /// <summary>
    /// The contract for handling inbound webhook callbacks from a vendor.
    /// Implemented per vendor alongside <see cref="IVendorAdapter"/>; both registered
    /// in the &lt;vendorAdapters&gt; config section.
    ///
    /// Two distinct phases:
    ///   1. ParseAndExtract — runs INLINE in the controller request thread on every receipt.
    ///      Pulls correlation keys out of the raw payload so the correlator can match
    ///      later without re-parsing. MUST be fast and MUST NOT throw.
    ///
    ///   2. FindMatchingTransactionAsync + OnConfirmedAsync — run on the background
    ///      <see cref="Persistence.WebhookCorrelator"/> thread. May do I/O. Should still
    ///      not throw; the correlator is defensive but bugs here can cause callback retries.
    /// </summary>
    public interface IInboundEventProcessor
    {
        /// <summary>The vendor name this processor handles. Must match its adapter's VendorName.</summary>
        string VendorName { get; }

        /// <summary>
        /// Extract correlation metadata from a raw webhook body. Called inline during
        /// the receive request — MUST be fast and MUST NOT throw. Return an empty
        /// <see cref="InboundEventMetadata"/> if parsing fails; the raw body is still
        /// persisted for offline forensics.
        /// </summary>
        InboundEventMetadata ParseAndExtract(string rawPayload);

        /// <summary>
        /// Find the outbound transaction this callback corresponds to. Called by the
        /// background correlator. Returns the matched TransactionId or null if no match.
        ///
        /// "No match" is normal (vendor sometimes sends callbacks for loads we never
        /// dispatched, e.g., loads created by another source) — the correlator marks
        /// the callback Processed and moves on.
        /// </summary>
        Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow callback,
            SqlConnection connection,
            CancellationToken cancellationToken);

        /// <summary>
        /// Called by the correlator AFTER a successful match, for vendor-specific
        /// side effects. Examples:
        /// - FK: stamp FourKitesLoadId on Vector's Load table for LOAD_CREATION callbacks.
        /// - P44: update local load status to mirror P44's status.
        ///
        /// Implementations should catch their own errors. The correlator continues
        /// regardless — vendor-specific side-effect failures should not block correlation.
        /// </summary>
        Task OnConfirmedAsync(
            InboundCallbackRow callback,
            long matchedTransactionId,
            SqlConnection connection,
            CancellationToken cancellationToken);
    }
}
