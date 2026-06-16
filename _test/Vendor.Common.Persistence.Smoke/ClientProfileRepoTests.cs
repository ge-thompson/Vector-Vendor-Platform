using System;
using System.Linq;
using System.Threading.Tasks;
using Vendor.Common.Configuration;

namespace Vendor.Common.Persistence.Smoke
{
    internal static class ClientProfileRepoTests
    {
        public static void RegisterAll()
        {
            // Test 1: reads seed FK row
            TestHarness.Run("ClientProfileRepository reads seed FK profile from DB", () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString);
                var all = repo.GetAllProfiles();
                TestHarness.Assert(all.Count > 0, "expected at least 1 profile row");

                var fk = all.FirstOrDefault(p =>
                    p.ShipperCode == "VECTOR_DEFAULT" && p.VendorName == "FourKites");
                TestHarness.AssertNotNull(fk, "FK profile row");
                TestHarness.Assert(fk.IsActive, "FK profile IsActive");
                TestHarness.AssertContains(fk.EnabledEvents, "LocationReportedEvent", "EnabledEvents CSV");
                TestHarness.AssertContains(fk.ConfigJson, "billToCode", "ConfigJson has billToCode key");
            });

            // Test 2: FindRouting matches via VECTOR_DEFAULT for an unknown shipper
            TestHarness.Run("ClientProfileRepository.FindRouting matches via VECTOR_DEFAULT", () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString);

                // Unknown shipper: should still match VECTOR_DEFAULT (the floor)
                var matches = repo.FindRouting("UNKNOWN_SHIPPER_X", "LocationReportedEvent");
                TestHarness.Assert(matches.Count >= 1,
                    "expected at least the VECTOR_DEFAULT row to match");

                var fk = matches.FirstOrDefault(m => m.VendorName == "FourKites");
                TestHarness.AssertNotNull(fk, "FK match in routing result");
                TestHarness.AssertEqual("VECTOR_DEFAULT", fk.ShipperCode, "matched via default");
            });

            // Test 3: FindRouting respects event type filter
            TestHarness.Run("ClientProfileRepository.FindRouting respects EnabledEvents", () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString);

                // Event not enabled on any profile → empty result
                var matches = repo.FindRouting("VECTOR_DEFAULT", "FakeEventNobodyEnabled");
                TestHarness.AssertEqual(0, matches.Count, "no matches for unknown event type");
            });

            // Test 4: cache returns same reference on repeated reads within TTL
            TestHarness.Run("ClientProfileRepository cache is hot within TTL", () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString,
                    cacheTtl: TimeSpan.FromMinutes(5));
                var first = repo.GetAllProfiles();
                var second = repo.GetAllProfiles();
                // Cache snapshot is shared — first and second should refer to the same IReadOnlyList instance
                TestHarness.Assert(ReferenceEquals(first, second),
                    "GetAllProfiles within TTL should return the cached snapshot");
            });

            // Test 5: InvalidateCache forces a re-read
            TestHarness.Run("ClientProfileRepository.InvalidateCache forces refresh", () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString,
                    cacheTtl: TimeSpan.FromMinutes(5));
                var first = repo.GetAllProfiles();
                repo.InvalidateCache();
                var second = repo.GetAllProfiles();
                // After invalidation, the snapshot is replaced — should be a different reference
                TestHarness.Assert(!ReferenceEquals(first, second),
                    "after InvalidateCache, GetAllProfiles should return a new snapshot");
                // But the content should still be equivalent
                TestHarness.AssertEqual(first.Count, second.Count, "same row count after refresh");
            });

            // Test 6: Async variant works
            TestHarness.RunAsync("ClientProfileRepository.GetAllProfilesAsync works", async () =>
            {
                var repo = new ClientProfileRepository(DbHelper.ConnectionString);
                var all = await repo.GetAllProfilesAsync().ConfigureAwait(false);
                TestHarness.Assert(all.Count > 0, "expected at least 1 profile row");
            }).GetAwaiter().GetResult();
        }
    }
}
