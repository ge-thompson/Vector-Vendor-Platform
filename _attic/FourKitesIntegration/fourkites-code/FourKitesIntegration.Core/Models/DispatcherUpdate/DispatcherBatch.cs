using System.Collections.Generic;
using FourKitesIntegration.Core.Models.Common;

namespace FourKitesIntegration.Core.Models.DispatcherUpdate
{
    /// <summary>
    /// Top-level envelope for POST /load/update/dispatcher-api/async.
    /// Wraps an array of updates, each targeting one load.
    /// </summary>
    public class DispatcherBatch
    {
        public List<LoadUpdateEntry> Updates { get; set; } = new List<LoadUpdateEntry>();
    }

    /// <summary>
    /// One entry in the updates[] array — represents all updates for ONE load.
    /// </summary>
    public class LoadUpdateEntry
    {
        /// <summary>IANA timezone name applied to all datetimes in this entry. Defaults to UTC if omitted.</summary>
        public string TimeZone { get; set; }

        /// <summary>
        /// Shipper identifier from FourKites Connect. Mandatory when sending multiple identifierKeys;
        /// recommended even with a single identifier.
        /// </summary>
        public string BillToCode { get; set; }

        /// <summary>
        /// Load matching keys (up to 4). FourKites tries each in array order until one matches.
        /// MUST be an array of objects, even with a single identifier.
        /// </summary>
        public List<IdentifierKey> IdentifierKeys { get; set; } = new List<IdentifierKey>();

        /// <summary>
        /// Array of update objects. Each object contains ONE update type (locationUpdate, eventUpdate, etc.).
        /// Multiple update types for the same load can be combined here.
        /// </summary>
        public List<LoadUpdatePayload> LoadUpdate { get; set; } = new List<LoadUpdatePayload>();
    }

    /// <summary>
    /// Union of all possible update types. Populate exactly ONE property per object instance.
    /// Newtonsoft will only serialize non-null members (NullValueHandling.Ignore in FourKitesJson).
    /// </summary>
    public class LoadUpdatePayload
    {
        public LocationUpdate LocationUpdate { get; set; }
        public EventUpdate EventUpdate { get; set; }
        public StopUpdate StopUpdate { get; set; }
        public AssignmentUpdate AssignmentUpdate { get; set; }
        public LoadInfoUpdate LoadInfoUpdate { get; set; }
        public EtaUpdate EtaUpdate { get; set; }
        public TemperatureUpdate TemperatureUpdate { get; set; }
    }
}
