using System;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.Common.Dispatch.Smoke
{
    /// <summary>
    /// Controllable test adapter that simulates every scenario the dispatcher
    /// needs to handle. Registered under vendorName="FourKites" so it matches
    /// the seed ClientProfile row.
    ///
    /// Tests configure behavior via the static Behavior property before dispatching,
    /// then read CallCount / LastEvent / LastProfile after the call to verify.
    ///
    /// IMPORTANT: this is the ADAPTER for the FourKites profile row in seed data.
    /// It reports VendorName="FourKites" so the registry binds it correctly.
    /// Despite the name "TestFourKitesAdapter", it doesn't actually call FK —
    /// it's a fully synthetic adapter.
    ///
    /// Constructor: parameterless. Exercises that code path in VendorAdapterRegistry.
    /// (The (ClientProfileRepository, Action&lt;Exception&gt;) constructor path is
    /// covered by the real FourKitesAdapter we'll build later.)
    /// </summary>
    public class TestFourKitesAdapter : IVendorAdapter
    {
        public string VendorName => "FourKites";

        // ─── Behavior controls ─────────────────────────────────────────────

        public enum BehaviorMode
        {
            ReturnSuccess,
            ReturnFailHttp,
            ReturnFailTransient,
            ReturnFailRateLimited,
            ReturnNull,                // contract violation — dispatcher should synthesize a failure
            ThrowInDispatch,           // contract violation — dispatcher should catch
            DeclineViaCanHandle        // CanHandle returns false
        }

        public static BehaviorMode Behavior { get; set; } = BehaviorMode.ReturnSuccess;

        // ─── Call recording for assertions ─────────────────────────────────

        public static int CanHandleCallCount;
        public static int DispatchCallCount;
        public static VendorEvent LastEvent;
        public static ClientProfile LastProfile;
        public static string LastResultStatus;

        /// <summary>Reset all static state between tests. Call before each test.</summary>
        public static void Reset()
        {
            Behavior = BehaviorMode.ReturnSuccess;
            CanHandleCallCount = 0;
            DispatchCallCount = 0;
            LastEvent = null;
            LastProfile = null;
            LastResultStatus = null;
        }

        // ─── IVendorAdapter ────────────────────────────────────────────────

        public bool CanHandle(VendorEvent evt)
        {
            Interlocked.Increment(ref CanHandleCallCount);
            if (Behavior == BehaviorMode.DeclineViaCanHandle) return false;
            return true;
        }

        public Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt,
            ClientProfile profile,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref DispatchCallCount);
            LastEvent = evt;
            LastProfile = profile;

            VendorOperationResult result;
            switch (Behavior)
            {
                case BehaviorMode.ReturnSuccess:
                    result = VendorOperationResult.Succeeded(
                        httpStatusCode: 202,
                        vendorRequestId: "test-req-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                        requestPayloadJson: "{\"test\":\"payload\"}",
                        responseBodyJson: "{\"status\":\"accepted\"}",
                        duration: TimeSpan.FromMilliseconds(15));
                    LastResultStatus = "Succeeded";
                    return Task.FromResult(result);

                case BehaviorMode.ReturnFailHttp:
                    result = VendorOperationResult.Failed(
                        "Bad request from vendor", "Permanent",
                        httpStatusCode: 400,
                        requestPayloadJson: "{\"test\":\"payload\"}",
                        responseBodyJson: "{\"error\":\"validation failed\"}");
                    LastResultStatus = "Failed-HTTP";
                    return Task.FromResult(result);

                case BehaviorMode.ReturnFailTransient:
                    result = VendorOperationResult.Failed(
                        new TimeoutException("simulated network timeout"), "Transient");
                    LastResultStatus = "Failed-Transient";
                    return Task.FromResult(result);

                case BehaviorMode.ReturnFailRateLimited:
                    result = VendorOperationResult.RateLimited("test rate limit");
                    LastResultStatus = "RateLimited";
                    return Task.FromResult(result);

                case BehaviorMode.ReturnNull:
                    LastResultStatus = "ReturnedNull";
                    return Task.FromResult<VendorOperationResult>(null);

                case BehaviorMode.ThrowInDispatch:
                    LastResultStatus = "Threw";
                    throw new InvalidOperationException("simulated adapter explosion");

                case BehaviorMode.DeclineViaCanHandle:
                    // Should not have reached here — CanHandle returned false. Treat as a bug.
                    LastResultStatus = "ShouldNotHaveDispatched";
                    return Task.FromResult(VendorOperationResult.Failed(
                        "Adapter dispatched despite CanHandle=false", "Unknown"));

                default:
                    throw new InvalidOperationException("Unknown behavior mode: " + Behavior);
            }
        }
    }
}
