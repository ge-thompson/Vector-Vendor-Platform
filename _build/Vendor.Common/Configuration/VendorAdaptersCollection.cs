using System.Configuration;

namespace Vendor.Common.Configuration
{
    /// <summary>
    /// Collection of &lt;add&gt; elements inside &lt;adapters&gt; inside &lt;vendorAdapters&gt;.
    /// .NET's <see cref="ConfigurationElementCollection"/> handles add/remove/clear semantics.
    /// </summary>
    [ConfigurationCollection(typeof(VendorAdapterElement),
        AddItemName = "add",
        ClearItemsName = "clear",
        RemoveItemName = "remove",
        CollectionType = ConfigurationElementCollectionType.AddRemoveClearMap)]
    public class VendorAdaptersCollection : ConfigurationElementCollection
    {
        protected override ConfigurationElement CreateNewElement()
            => new VendorAdapterElement();

        protected override object GetElementKey(ConfigurationElement element)
            => ((VendorAdapterElement)element).VendorName;

        /// <summary>Index access by position (0-based).</summary>
        public VendorAdapterElement this[int index]
        {
            get => (VendorAdapterElement)BaseGet(index);
        }

        /// <summary>Index access by vendor name. Returns null if not found.</summary>
        public new VendorAdapterElement this[string vendorName]
            => (VendorAdapterElement)BaseGet(vendorName);
    }
}
