using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Reads X-RateLimit headers from FourKites responses and provides proactive throttling.
    /// Default limit is 60 req/min per apikey. Updates atomically — safe to share across threads on
    /// a single FourKitesClient instance.
    /// </summary>
    public sealed class RateLimitTracker
    {
        private int _remaining = 60;
        private int _limit = 60;
        private long _windowStartUtcTicks = DateTime.UtcNow.Ticks;

        public int Remaining => Volatile.Read(ref _remaining);
        public int Limit => Volatile.Read(ref _limit);

        /// <summary>
        /// Update tracker from response headers. Call after every API call.
        /// </summary>
        public void UpdateFromResponse(HttpResponseMessage response)
        {
            if (response == null) return;

            if (response.Headers.TryGetValues("X-RateLimit-Remaining-minute", out var remVals))
            {
                foreach (var v in remVals)
                {
                    if (int.TryParse(v, out int n))
                    {
                        // If we see the limit value, the window has just reset.
                        int currentLimit = Volatile.Read(ref _limit);
                        if (n >= currentLimit)
                            Interlocked.Exchange(ref _windowStartUtcTicks, DateTime.UtcNow.Ticks);
                        Interlocked.Exchange(ref _remaining, n);
                        break;
                    }
                }
            }

            if (response.Headers.TryGetValues("X-RateLimit-Limit-minute", out var limVals))
            {
                foreach (var v in limVals)
                {
                    if (int.TryParse(v, out int n))
                    {
                        Interlocked.Exchange(ref _limit, n);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// If we're getting close to the limit, sleep until the window resets.
        /// Call this BEFORE making a request.
        /// </summary>
        public async Task ThrottleIfNeededAsync(CancellationToken ct = default)
        {
            int rem = Remaining;
            if (rem > 10) return; // plenty of headroom

            var windowStart = new DateTime(Interlocked.Read(ref _windowStartUtcTicks), DateTimeKind.Utc);
            var secondsUntilReset = 60 - (DateTime.UtcNow - windowStart).TotalSeconds;
            if (secondsUntilReset <= 0) return; // window already reset, just go

            // Spread remaining quota over remaining seconds.
            int delayMs = (int)Math.Max(100, (secondsUntilReset * 1000.0) / Math.Max(1, rem));
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
    }
}
