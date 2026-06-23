using System.Configuration;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// One row in the &lt;vendorAdapters&gt; config section. Represents one vendor's
    /// pluggable types — its outbound adapter, optional inbound processor, optional
    /// webhook signature validator.
    ///
    /// Example config:
    /// <code>
    /// &lt;add vendorName="ExampleVendor"
    ///      adapterType="Vendor.ExampleVendor.Adapter.ExampleVendorAdapter, Vendor.ExampleVendor"
    ///      inboundProcessorType="Vendor.ExampleVendor.Webhooks.ExampleVendorWebhookProcessor, Vendor.ExampleVendor"
    ///      webhookValidatorType="Vendor.ExampleVendor.Webhooks.ExampleVendorWebhookSignatureValidator, Vendor.ExampleVendor" /&gt;
    /// </code>
    ///
    /// Only adapterType is required. Inbound types are optional — desktop callers
    /// (e.g., VB.NET POD app) don't host webhook endpoints so they leave these blank.
    /// </summary>
    public class VendorAdapterElement : ConfigurationElement
    {
        /// <summary>
        /// Vendor identifier (e.g., "ExampleVendor"). Must match the VendorName property
        /// on the adapter class and the VendorName column on ClientProfiles rows.
        /// </summary>
        [ConfigurationProperty("vendorName", IsRequired = true, IsKey = true)]
        public string VendorName
        {
            get => (string)this["vendorName"];
            set => this["vendorName"] = value;
        }

        /// <summary>
        /// Assembly-qualified type name of the IVendorAdapter implementation.
        /// Format: "Namespace.ClassName, AssemblyName".
        /// </summary>
        [ConfigurationProperty("adapterType", IsRequired = true)]
        public string AdapterType
        {
            get => (string)this["adapterType"];
            set => this["adapterType"] = value;
        }

        /// <summary>
        /// Assembly-qualified type name of the IInboundEventProcessor implementation.
        /// Optional — leave empty for vendors that don't send webhooks, or for callers
        /// that don't host the webhook endpoint.
        /// </summary>
        [ConfigurationProperty("inboundProcessorType", IsRequired = false, DefaultValue = "")]
        public string InboundProcessorType
        {
            get => (string)this["inboundProcessorType"];
            set => this["inboundProcessorType"] = value;
        }

        /// <summary>
        /// Assembly-qualified type name of the IWebhookSignatureValidator implementation.
        /// Optional — required only if InboundProcessorType is set.
        /// </summary>
        [ConfigurationProperty("webhookValidatorType", IsRequired = false, DefaultValue = "")]
        public string WebhookValidatorType
        {
            get => (string)this["webhookValidatorType"];
            set => this["webhookValidatorType"] = value;
        }
    }
}
