namespace FourKitesIntegration.Core.Models.Common
{
    /// <summary>
    /// One entry in the identifierKeys[] array used by Dispatcher Update calls to find a load.
    /// FourKites tries each identifier in array order until one matches.
    /// </summary>
    public class IdentifierKey
    {
        /// <summary>The reference value FourKites should match against (e.g. "BOL12345"). REQUIRED.</summary>
        public string Identifier { get; set; }

        /// <summary>Your TMS-formatted version (informational only). OPTIONAL.</summary>
        public string RawIdentifier { get; set; }

        /// <summary>
        /// FourKites-defined values: "loadNumber", "proNumber", "loadReferenceNumber",
        /// "loadTrackingNumber", "carrierReferenceNumber". Any other value treated as informational only.
        /// </summary>
        public string IdentifierType { get; set; }
    }

    /// <summary>Well-known identifier types FourKites recognizes for matching.</summary>
    public static class IdentifierTypes
    {
        public const string LoadNumber = "loadNumber";
        public const string ProNumber = "proNumber";
        public const string LoadReferenceNumber = "loadReferenceNumber";
        public const string LoadTrackingNumber = "loadTrackingNumber";
        public const string CarrierReferenceNumber = "carrierReferenceNumber";
    }
}
