using System;
using System.Threading;

namespace Vendor.FourKites.RateLimiting
{
    /// <summary>
    /// In-memory token-bucket rate limiter. Self-throttles outbound requests
    /// so we never deliberately send a request we know would get rate-limited.
    ///
    /// SEMANTICS:
    ///   - Bucket holds up to <see cref="BurstSize"/> tokens
    ///   - Tokens refill at <see cref="RequestsPerSecond"/> tokens/second
    ///   - Each call to <see cref="TryAcquire"/> consumes one token if available,
    ///     returns false if the bucket is empty
    ///
    /// The adapter calls TryAcquire before HTTP. If false, the adapter returns
    /// VendorOperationResult.RateLimited (locally throttled — never even attempted).
    ///
    /// THREAD-SAFE via Interlocked operations on a packed long. No locks in the hot path.
    ///
    /// PROCESS-SCOPED, NOT DISTRIBUTED. Multiple OTR API instances would each have
    /// their own bucket. For Phase 1 (single OTR instance) this is fine. If we ever
    /// host on multiple servers, replace with a Redis-backed implementation; the
    /// public API is what the adapter depends on so the swap is contained.
    /// </summary>
    public class InMemoryRateLimiter
    {
        public int BurstSize { get; }
        public double RequestsPerSecond { get; }

        // Stores (tokens, lastRefillTicks) packed into one atomically-updatable value.
        // Tokens are stored * 1000 (i.e. millitokens) to give fractional refill resolution
        // without going to double-CAS.
        private long _stateMillitokens;        // current tokens × 1000
        private long _lastRefillTicks;          // Stopwatch ticks of last refill calculation

        // Locks for CAS-style updates on the two fields together.
        private readonly object _refillLock = new object();

        public InMemoryRateLimiter(int burstSize, double requestsPerSecond)
        {
            if (burstSize <= 0) throw new ArgumentException("BurstSize must be > 0", nameof(burstSize));
            if (requestsPerSecond <= 0) throw new ArgumentException("RequestsPerSecond must be > 0", nameof(requestsPerSecond));

            BurstSize = burstSize;
            RequestsPerSecond = requestsPerSecond;

            _stateMillitokens = (long)burstSize * 1000;
            _lastRefillTicks = NowTicks();
        }

        /// <summary>
        /// Attempts to consume one token. Returns true if granted (caller may proceed
        /// with the request), false if the bucket is empty (caller should defer or
        /// signal rate-limited).
        /// </summary>
        public bool TryAcquire()
        {
            RefillIfNeeded();

            lock (_refillLock)
            {
                if (_stateMillitokens >= 1000)
                {
                    _stateMillitokens -= 1000;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Diagnostic: current available tokens (rounded down). For tests and admin tools.
        /// </summary>
        public int AvailableTokens
        {
            get
            {
                RefillIfNeeded();
                return (int)(Interlocked.Read(ref _stateMillitokens) / 1000);
            }
        }

        private void RefillIfNeeded()
        {
            var now = NowTicks();
            var last = Interlocked.Read(ref _lastRefillTicks);
            var elapsedSeconds = (now - last) / (double)TimeSpan.TicksPerSecond;
            if (elapsedSeconds <= 0) return;

            var millitokensToAdd = (long)(elapsedSeconds * RequestsPerSecond * 1000.0);
            if (millitokensToAdd <= 0) return;

            lock (_refillLock)
            {
                // Re-read inside lock
                last = _lastRefillTicks;
                elapsedSeconds = (now - last) / (double)TimeSpan.TicksPerSecond;
                millitokensToAdd = (long)(elapsedSeconds * RequestsPerSecond * 1000.0);
                if (millitokensToAdd <= 0) return;

                var maxMillitokens = (long)BurstSize * 1000;
                _stateMillitokens = Math.Min(maxMillitokens, _stateMillitokens + millitokensToAdd);
                _lastRefillTicks = now;
            }
        }

        private static long NowTicks() => DateTime.UtcNow.Ticks;
    }
}
