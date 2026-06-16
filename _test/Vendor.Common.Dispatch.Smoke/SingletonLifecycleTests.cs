using System;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Dispatch;
using Vendor.Common.Persistence;

namespace Vendor.Common.Dispatch.Smoke
{
    internal static class SingletonLifecycleTests
    {
        public static void RegisterAll()
        {
            TestHarness.Run("A1. Instance throws cleanly before Configure", () =>
            {
                VendorDispatcher.ResetForTesting();
                TestHarness.Assert(!VendorDispatcher.IsConfigured, "IsConfigured should be false initially");
                TestHarness.AssertThrows<InvalidOperationException>(
                    () => { var _ = VendorDispatcher.Instance; },
                    "accessing Instance before Configure");
            });

            TestHarness.Run("A2. ConfigureForTesting populates Instance", () =>
            {
                VendorDispatcher.ResetForTesting();

                var profileRepo = new ClientProfileRepository(DbHelper.ConnectionString);
                var auditRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var registry = new VendorAdapterRegistry(new IVendorAdapter[] { new TestFourKitesAdapter() });
                var resolver = new LoadShipperResolver();

                VendorDispatcher.ConfigureForTesting(
                    enabled: true,
                    fireAndForget: false,
                    registry: registry,
                    profileRepository: profileRepo,
                    auditRepository: auditRepo,
                    shipperResolver: resolver);

                TestHarness.Assert(VendorDispatcher.IsConfigured, "IsConfigured should be true after Configure");
                TestHarness.AssertNotNull(VendorDispatcher.Instance, "Instance");
            });

            TestHarness.Run("A3. IsConfigured tracks state correctly", () =>
            {
                VendorDispatcher.ResetForTesting();
                TestHarness.Assert(!VendorDispatcher.IsConfigured, "before configure");

                var profileRepo = new ClientProfileRepository(DbHelper.ConnectionString);
                var auditRepo = new OutboundTransactionRepository(DbHelper.ConnectionString);
                var registry = new VendorAdapterRegistry(new IVendorAdapter[] { new TestFourKitesAdapter() });
                VendorDispatcher.ConfigureForTesting(true, false, registry, profileRepo, auditRepo, new LoadShipperResolver());

                TestHarness.Assert(VendorDispatcher.IsConfigured, "after configure");

                VendorDispatcher.ResetForTesting();
                TestHarness.Assert(!VendorDispatcher.IsConfigured, "after reset");
            });
        }
    }
}
