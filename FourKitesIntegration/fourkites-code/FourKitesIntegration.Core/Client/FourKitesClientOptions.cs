namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Configuration for FourKitesClient. Inject via constructor.
    /// </summary>
    public class FourKitesClientOptions
    {
        /// <summary>Environment identifier — for logging only. Either "Staging" or "Production".</summary>
        public string Environment { get; set; } = "Staging";

        /// <summary>Host (no scheme, no trailing slash). e.g. "api.fourkites.com" or "api-staging.fourkites.com".</summary>
        public string BaseHost { get; set; } = "api-staging.fourkites.com";

        /// <summary>The apikey header value. Load from a credential vault, NEVER hardcode.</summary>
        public string ApiKey { get; set; }

        /// <summary>Per-request HTTP timeout. FourKites' async endpoint is fast (sub-second normally).</summary>
        public System.TimeSpan HttpTimeout { get; set; } = System.TimeSpan.FromSeconds(30);

        /// <summary>How many times to retry on transient (5xx, 429) failures before giving up.</summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>Default BillToCode used by smoke tests; production code should always pass explicitly.</summary>
        public string DefaultBillToCode { get; set; }
    }
}
