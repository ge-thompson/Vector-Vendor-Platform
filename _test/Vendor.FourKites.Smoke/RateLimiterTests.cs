using System;
using System.Threading;
using Vendor.FourKites.RateLimiting;

namespace Vendor.FourKites.Smoke
{
    internal static class RateLimiterTests
    {
        public static void RegisterAll()
        {
            TestHarness.Run("RateLimiter. Allows burst up to BurstSize tokens", () =>
            {
                var rl = new InMemoryRateLimiter(burstSize: 5, requestsPerSecond: 1);

                // Should grant 5 immediate acquires (burst capacity)
                for (int i = 0; i < 5; i++)
                {
                    TestHarness.Assert(rl.TryAcquire(), $"acquire #{i + 1} should succeed");
                }
                // 6th immediate acquire should fail (bucket empty)
                TestHarness.Assert(!rl.TryAcquire(), "acquire #6 should fail (bucket empty)");
            });

            TestHarness.Run("RateLimiter. Refills at configured rate", () =>
            {
                var rl = new InMemoryRateLimiter(burstSize: 2, requestsPerSecond: 10);

                // Drain
                TestHarness.Assert(rl.TryAcquire(), "drain 1");
                TestHarness.Assert(rl.TryAcquire(), "drain 2");
                TestHarness.Assert(!rl.TryAcquire(), "drained");

                // Wait long enough to refill at least one token
                // At 10 rps, 150ms should yield ~1.5 tokens
                Thread.Sleep(200);

                TestHarness.Assert(rl.TryAcquire(),
                    "after 200ms at 10rps, should have refilled enough for 1 acquire");
            });

            TestHarness.Run("RateLimiter. Refill respects BurstSize ceiling", () =>
            {
                var rl = new InMemoryRateLimiter(burstSize: 3, requestsPerSecond: 100);

                // Wait long enough to overfill — refill should cap at BurstSize
                Thread.Sleep(200);

                TestHarness.AssertEqual(3, rl.AvailableTokens,
                    "AvailableTokens should cap at BurstSize");
            });

            TestHarness.Run("RateLimiter. Constructor validates inputs", () =>
            {
                TestHarness.AssertThrows<ArgumentException>(
                    () => new InMemoryRateLimiter(0, 10),
                    "burstSize=0 should throw");
                TestHarness.AssertThrows<ArgumentException>(
                    () => new InMemoryRateLimiter(10, 0),
                    "rps=0 should throw");
            });
        }
    }
}
