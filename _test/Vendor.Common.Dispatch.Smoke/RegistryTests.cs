using System;
using System.Configuration;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Events;

namespace Vendor.Common.Dispatch.Smoke
{
    internal static class RegistryTests
    {
        public static void RegisterAll()
        {
            TestHarness.Run("B1. Registry loads adapter from config via reflection", () =>
            {
                // Use the actual <vendorAdapters> section in App.config — same path production uses
                var section = VendorAdaptersSection.Load();
                TestHarness.AssertNotNull(section, "vendorAdapters section");

                var registry = new VendorAdapterRegistry(section);
                var adapter = registry.GetAdapter("FourKites");
                TestHarness.AssertNotNull(adapter, "FourKites adapter from reflection");
                TestHarness.AssertEqual("FourKites", adapter.VendorName, "adapter.VendorName");
                TestHarness.Assert(adapter is TestFourKitesAdapter,
                    "adapter should be TestFourKitesAdapter (as declared in App.config)");
            });

            TestHarness.Run("B2. Registry throws on missing type with helpful message", () =>
            {
                // Build a section by hand with a bogus type
                var section = BuildSyntheticSection("FakeVendor",
                    "Nonexistent.Type.That.Does.Not.Exist, Nonexistent.Assembly");

                try
                {
                    var registry = new VendorAdapterRegistry(section);
                    throw new Exception("expected VendorAdapterRegistryException");
                }
                catch (VendorAdapterRegistryException ex)
                {
                    TestHarness.AssertContains(ex.Message, "could not load type",
                        "exception message should explain the type-loading failure");
                }
            });

            TestHarness.Run("B3. Registry throws on VendorName mismatch", () =>
            {
                // Declare a vendor name that doesn't match the TestFourKitesAdapter (which reports "FourKites")
                var section = BuildSyntheticSection("WrongVendorName",
                    "Vendor.Common.Dispatch.Smoke.TestFourKitesAdapter, Vendor.Common.Dispatch.Smoke");

                try
                {
                    var registry = new VendorAdapterRegistry(section);
                    throw new Exception("expected VendorAdapterRegistryException for VendorName mismatch");
                }
                catch (VendorAdapterRegistryException ex)
                {
                    TestHarness.AssertContains(ex.Message, "VendorName",
                        "exception should mention VendorName mismatch");
                }
            });
        }

        /// <summary>
        /// Builds a VendorAdaptersSection in memory by reflection-poking the
        /// underlying collection. Avoids needing to write a temp .config file.
        /// </summary>
        private static VendorAdaptersSection BuildSyntheticSection(
            string vendorName, string adapterType)
        {
            var section = new VendorAdaptersSection();
            var element = new VendorAdapterElement
            {
                VendorName = vendorName,
                AdapterType = adapterType
            };

            // ConfigurationElementCollection exposes BaseAdd via protected — we
            // reach via reflection. Acceptable in test code.
            var collection = section.Adapters;
            var baseAdd = typeof(System.Configuration.ConfigurationElementCollection)
                .GetMethod("BaseAdd",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(System.Configuration.ConfigurationElement) },
                    null);
            baseAdd.Invoke(collection, new object[] { element });
            return section;
        }
    }
}
