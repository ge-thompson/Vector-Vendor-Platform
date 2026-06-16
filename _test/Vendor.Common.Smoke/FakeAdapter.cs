using System;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;
using Vendor.Common.Configuration;
using Vendor.Common.Events;

namespace Vendor.Common.Smoke
{
    /// <summary>
    /// A minimal IVendorAdapter implementation used to prove the contract is usable.
    /// Doesn't talk to any real vendor — just records what it was asked to dispatch
    /// and returns a synthetic result.
    /// </summary>
    internal class FakeAdapter : IVendorAdapter
    {
        public string VendorName => "Fake";

        public int CanHandleCallCount { get; private set; }
        public int DispatchCallCount { get; private set; }
        public VendorEvent LastEvent { get; private set; }
        public ClientProfile LastProfile { get; private set; }

        public bool CanHandle(VendorEvent evt)
        {
            CanHandleCallCount++;
            // Pretend we handle everything except GenericLoadEvent
            return !(evt is GenericLoadEvent);
        }

        public Task<VendorOperationResult> DispatchAsync(
            VendorEvent evt,
            ClientProfile profile,
            CancellationToken cancellationToken = default)
        {
            DispatchCallCount++;
            LastEvent = evt;
            LastProfile = profile;

            var result = VendorOperationResult.Succeeded(
                httpStatusCode: 202,
                vendorRequestId: Guid.NewGuid().ToString(),
                requestPayloadJson: "{ \"fake\": true }",
                responseBodyJson:  "{ \"status\": \"accepted\" }",
                duration: TimeSpan.FromMilliseconds(42));
            return Task.FromResult(result);
        }
    }

    /// <summary>Minimal IInboundEventProcessor — same idea, for the inbound contract.</summary>
    internal class FakeInboundProcessor : IInboundEventProcessor
    {
        public string VendorName => "Fake";

        public InboundEventMetadata ParseAndExtract(string rawPayload)
        {
            // Pretend the body always contains LoadId="LOAD999"
            return new InboundEventMetadata
            {
                MessageType = "FAKE_EVENT",
                VendorLoadId = "FAKE-999",
                VectorLoadId = "LOAD999",
                IsSuccess = true
            };
        }

        public Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow callback,
            System.Data.SqlClient.SqlConnection connection,
            CancellationToken cancellationToken)
            => Task.FromResult<long?>(null);  // never matches in the fake

        public Task OnConfirmedAsync(
            InboundCallbackRow callback,
            long matchedTransactionId,
            System.Data.SqlClient.SqlConnection connection,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
