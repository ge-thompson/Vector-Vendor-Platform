using System.Configuration;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// Custom configuration section for declaring which vendor adapters are loaded.
    /// Registered in Web.config / App.config &lt;configSections&gt; like so:
    ///
    /// <code>
    /// &lt;configSections&gt;
    ///   &lt;section name="vendorAdapters"
    ///            type="Vendor.Common.Configuration.VendorAdaptersSection, Vendor.Common" /&gt;
    /// &lt;/configSections&gt;
    ///
    /// &lt;vendorAdapters&gt;
    ///   &lt;adapters&gt;
    ///     &lt;add vendorName="FourKites"
    ///          adapterType="Vendor.FourKites.Adapter.FourKitesAdapter, Vendor.FourKites"
    ///          inboundProcessorType="Vendor.FourKites.Webhooks.FourKitesWebhookProcessor, Vendor.FourKites"
    ///          webhookValidatorType="Vendor.FourKites.Webhooks.FourKitesWebhookSignatureValidator, Vendor.FourKites" /&gt;
    ///   &lt;/adapters&gt;
    /// &lt;/vendorAdapters&gt;
    /// </code>
    ///
    /// Adding a vendor #2 is one line in this section. That's the resale story made concrete.
    /// </summary>
    public class VendorAdaptersSection : ConfigurationSection
    {
        /// <summary>Section name as referenced in callers' ConfigurationManager.GetSection().</summary>
        public const string SectionName = "vendorAdapters";

        [ConfigurationProperty("adapters", IsDefaultCollection = false)]
        public VendorAdaptersCollection Adapters
            => (VendorAdaptersCollection)this["adapters"];

        /// <summary>
        /// Convenience loader. Returns null if the section isn't declared (which is
        /// valid — callers without webhook hosting may not declare it).
        /// </summary>
        public static VendorAdaptersSection Load()
        {
            return ConfigurationManager.GetSection(SectionName) as VendorAdaptersSection;
        }
    }
}
