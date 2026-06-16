using System;

namespace Vendor.Common.Events
{
    /// <summary>
    /// A document (POD, BOL, rate confirmation, etc.) is ready to attach to a load.
    /// Carries the file contents in-memory; adapters POST as multipart/form-data to
    /// their vendor's documents endpoint.
    ///
    /// Source of truth (per Phase 2 dedup policy): the VB.NET POD app for delivery
    /// receipts; FBS for rate confirmations; future callers as needed.
    /// </summary>
    public class DocumentAvailableEvent : VendorEvent
    {
        public DocumentType DocumentType { get; set; }

        /// <summary>Original filename for vendor display (e.g., "pod-12345.pdf").</summary>
        public string FileName { get; set; }

        /// <summary>MIME type of the document (e.g., "application/pdf", "image/jpeg").</summary>
        public string MimeType { get; set; }

        /// <summary>Raw file bytes. Adapters must not retain references after dispatch.</summary>
        public byte[] Content { get; set; }

        /// <summary>When the document was captured/scanned. Optional but recommended.</summary>
        public DateTime? CapturedUtc { get; set; }
    }
}
