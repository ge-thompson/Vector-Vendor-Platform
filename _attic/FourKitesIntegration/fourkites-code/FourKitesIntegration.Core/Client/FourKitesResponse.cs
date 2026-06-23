using System;

namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Unified response envelope from any FourKites API call. The dispatcher endpoint always returns 202;
    /// other endpoints may return 2xx with different bodies. Capture and persist requestId for support tickets.
    /// </summary>
    public class FourKitesResponse
    {
        /// <summary>HTTP status code returned by the API.</summary>
        public int StatusCode { get; set; }

        /// <summary>The requestId UUID, sourced from response header X-Request-Id OR the JSON body's requestId field.</summary>
        public string RequestId { get; set; }

        /// <summary>Remaining rate-limit quota for this minute (X-RateLimit-Remaining-minute header). -1 if not present.</summary>
        public int RateLimitRemaining { get; set; } = -1;

        /// <summary>The configured rate-limit ceiling (X-RateLimit-Limit-minute header). -1 if not present.</summary>
        public int RateLimitLimit { get; set; } = -1;

        /// <summary>Raw JSON response body. Always populated for non-empty responses.</summary>
        public string Body { get; set; }

        /// <summary>Classification used by retry policy.</summary>
        public FourKitesErrorClass ErrorClass { get; set; }

        /// <summary>If FourKites returned a parsed errorMessage field, populated here.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>If FourKites returned a parsed errorCode field, populated here.</summary>
        public int? ErrorCode { get; set; }

        /// <summary>When the call was made (UTC).</summary>
        public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>For client-side troubleshooting only — exception detail if HTTP transport failed entirely.</summary>
        public string TransportException { get; set; }

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    }
}
