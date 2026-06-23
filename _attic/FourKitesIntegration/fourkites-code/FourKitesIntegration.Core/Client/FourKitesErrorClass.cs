using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Classification of API responses for retry policy decisions.
    /// </summary>
    public enum FourKitesErrorClass
    {
        Success,
        BadRequest,       // 400 — malformed payload; do NOT retry, fix code
        Unauthorized,     // 401 — bad API key
        Forbidden,        // 403 — FK 8887 — valid key, no permission
        NotFound,         // 404 — FK 8886
        MethodNotAllowed, // 405 — FK 2003
        Conflict,         // 409 — possible duplicate, do NOT immediately retry
        UnsupportedMedia, // 415 — Content-Type wrong
        RateLimited,      // 429 — back off
        ServerError,      // 5xx, FK 9999 — retry with backoff
        TransportFailure, // Network failure before HTTP response received
        Unknown
    }

    public static class FourKitesErrorClassifier
    {
        /// <summary>
        /// Inspect an HTTP response and (optionally) the parsed JSON body to classify the result.
        /// </summary>
        public static FourKitesErrorClass Classify(HttpResponseMessage response, string body)
        {
            if (response == null) return FourKitesErrorClass.TransportFailure;
            if (response.IsSuccessStatusCode) return FourKitesErrorClass.Success;

            // Attempt to extract FourKites custom error code for finer-grained classification.
            int? fkErrorCode = TryExtractErrorCode(body);

            int status = (int)response.StatusCode;
            switch (status)
            {
                case 400: return FourKitesErrorClass.BadRequest;
                case 401: return FourKitesErrorClass.Unauthorized;
                case 403: return FourKitesErrorClass.Forbidden;
                case 404: return FourKitesErrorClass.NotFound;
                case 405: return FourKitesErrorClass.MethodNotAllowed;
                case 409: return FourKitesErrorClass.Conflict;
                case 415: return FourKitesErrorClass.UnsupportedMedia;
                case 429: return FourKitesErrorClass.RateLimited;
                default:
                    if (status >= 500 && status < 600) return FourKitesErrorClass.ServerError;
                    return FourKitesErrorClass.Unknown;
            }
        }

        public static bool IsRetryable(FourKitesErrorClass cls)
        {
            switch (cls)
            {
                case FourKitesErrorClass.RateLimited:
                case FourKitesErrorClass.ServerError:
                case FourKitesErrorClass.TransportFailure:
                    return true;
                default:
                    return false;
            }
        }

        private static int? TryExtractErrorCode(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                var obj = JObject.Parse(body);
                var code = obj["errorCode"];
                if (code != null && code.Type == JTokenType.Integer)
                    return code.Value<int>();
            }
            catch { /* default error envelope or malformed — fine */ }
            return null;
        }
    }
}
