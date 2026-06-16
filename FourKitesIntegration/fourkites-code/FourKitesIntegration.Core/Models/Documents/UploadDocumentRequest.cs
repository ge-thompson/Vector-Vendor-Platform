using System.Collections.Generic;

namespace FourKitesIntegration.Core.Models.Documents
{
    /// <summary>
    /// POST https://api.fourkites.com/document-data/upload
    /// Documents are base64-encoded inside the JSON body (NOT multipart upload).
    /// Max file size: 10 MB per call. Supported formats: PDF, JPEG, TIFF.
    /// </summary>
    public class UploadDocumentRequest
    {
        public DocumentLoadRef Load { get; set; }
        public List<DocumentAttachment> Documents { get; set; }
    }

    public class DocumentLoadRef
    {
        public string Identifier { get; set; }    // "loadNumber" | "proNumber" | "trackingNumber" | "loadReferenceNumber"
        public string Value { get; set; }
    }

    public class DocumentAttachment
    {
        public DocumentStopRef Stop { get; set; }       // optional — defaults to last stop
        public string Type { get; set; }                // "pdf" | "jpeg" | "tiff"
        public string Document_type { get; set; }       // FourKites uses underscore here; see DocumentTypeCodes
        public string Base64_content { get; set; }      // FourKites uses underscore here
    }

    public class DocumentStopRef
    {
        public string Identifier { get; set; }  // "stopReference" | "stopId" | "locationId" | "stopSequence"
        public string Value { get; set; }
    }

    /// <summary>
    /// GET /document-data response shape.
    /// </summary>
    public class DocumentDownloadResult
    {
        public string Base64_content { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Document type codes recognized by FourKites.
    /// </summary>
    public static class DocumentTypeCodes
    {
        public const string BillOfLadingDriver = "BLD";
        public const string PackingSlipDriver = "PSD";
        public const string BillOfLading = "BL";
        public const string PackingSlip = "PS";
        public const string CustomsDocument = "CD";
        public const string ProofOfDelivery = "DR";        // Delivery Receipt — your POD
        public const string Invoice = "IV";
        public const string WeightInspectionCert = "WC";
        public const string WeightInspection = "WI";
        public const string WeightResearchPicture = "WP";
        public const string FreightBill = "FB";
        public const string PurchaseOrder = "PO";
        public const string OverageShortageDamage = "OD";
        public const string FuelAdvance = "FA";
        public const string LumperReceipt = "LR";
    }

    public static class DocumentFileTypes
    {
        public const string Pdf = "pdf";
        public const string Jpeg = "jpeg";
        public const string Tiff = "tiff";
    }
}
