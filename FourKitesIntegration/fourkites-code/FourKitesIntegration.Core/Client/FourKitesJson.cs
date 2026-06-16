using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace FourKitesIntegration.Core.Client
{
    /// <summary>
    /// Centralized Newtonsoft.Json settings for FourKites payloads.
    /// Properties are serialized camelCase; null values are omitted to match the API's optional-field convention.
    /// </summary>
    public static class FourKitesJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.None
        };

        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
