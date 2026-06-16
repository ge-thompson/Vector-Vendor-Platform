using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Vendor.Common.Abstractions;

namespace Vendor.Common.Persistence.Smoke
{
    /// <summary>
    /// Controllable IInboundEventProcessor for WebhookCorrelator tests. Same idea as
    /// TestFourKitesAdapter from the dispatch smoke — static behavior controls, call
    /// recording for assertions.
    ///
    /// Reports VendorName="FourKites" so it binds to the existing seed data and the
    /// vendor names other repositories use.
    /// </summary>
    public class TestFourKitesInboundProcessor : IInboundEventProcessor
    {
        public string VendorName => "FourKites";

        public enum BehaviorMode
        {
            ReturnMatch,                       // FindMatchingTransactionAsync returns _matchTransactionId
            ReturnNull,                        // returns null (vendor sent callback for unknown load)
            ThrowInFind,                       // FindMatchingTransactionAsync throws
            ThrowInOnConfirmed                 // FindMatchingTransactionAsync succeeds, OnConfirmedAsync throws
        }

        public static BehaviorMode Behavior { get; set; } = BehaviorMode.ReturnMatch;

        /// <summary>The TransactionId returned by FindMatchingTransactionAsync when Behavior = ReturnMatch.</summary>
        public static long MatchTransactionId { get; set; }

        // Call recording for assertions
        public static int ParseAndExtractCallCount;
        public static int FindMatchingCallCount;
        public static int OnConfirmedCallCount;
        public static InboundCallbackRow LastCallback;
        public static long LastMatchedTxId;

        public static void Reset()
        {
            Behavior = BehaviorMode.ReturnMatch;
            MatchTransactionId = 0;
            ParseAndExtractCallCount = 0;
            FindMatchingCallCount = 0;
            OnConfirmedCallCount = 0;
            LastCallback = null;
            LastMatchedTxId = 0;
        }

        public InboundEventMetadata ParseAndExtract(string rawPayload)
        {
            Interlocked.Increment(ref ParseAndExtractCallCount);
            return new InboundEventMetadata { MessageType = "TEST", IsSuccess = true };
        }

        public Task<long?> FindMatchingTransactionAsync(
            InboundCallbackRow callback, SqlConnection connection, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref FindMatchingCallCount);
            LastCallback = callback;

            switch (Behavior)
            {
                case BehaviorMode.ReturnMatch:
                    return Task.FromResult<long?>(MatchTransactionId);
                case BehaviorMode.ReturnNull:
                    return Task.FromResult<long?>(null);
                case BehaviorMode.ThrowInFind:
                    throw new InvalidOperationException("simulated processor explosion in Find");
                case BehaviorMode.ThrowInOnConfirmed:
                    return Task.FromResult<long?>(MatchTransactionId);
                default:
                    throw new InvalidOperationException("Unknown behavior: " + Behavior);
            }
        }

        public Task OnConfirmedAsync(
            InboundCallbackRow callback, long matchedTransactionId,
            SqlConnection connection, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref OnConfirmedCallCount);
            LastMatchedTxId = matchedTransactionId;

            if (Behavior == BehaviorMode.ThrowInOnConfirmed)
                throw new InvalidOperationException("simulated processor explosion in OnConfirmed");

            return Task.CompletedTask;
        }
    }
}
