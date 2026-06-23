using System;
using System.Globalization;

namespace FourKitesIntegration.Core.Models.Common
{
    /// <summary>
    /// Timestamp formatting helpers. FourKites accepts ISO 8601 / RFC3339; the timeZone field uses IANA names.
    /// .NET Framework 4.8.1 does NOT have built-in TimeZoneInfo.TryConvertWindowsIdToIanaId — use the
    /// TimeZoneConverter NuGet package if you need to convert Windows TZ IDs to IANA names.
    /// </summary>
    public static class FourKitesTime
    {
        /// <summary>UTC timestamp in canonical "Z" form, e.g. "2026-05-18T14:30:00Z".</summary>
        public static string FormatUtc(DateTimeOffset dt) =>
            dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        /// <summary>Local time with offset, e.g. "2026-05-18T09:30:00-05:00".</summary>
        public static string FormatLocal(DateTimeOffset dt) =>
            dt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        /// <summary>Convert DateTime to ISO 8601 UTC. If the input has Unspecified Kind, it is assumed local.</summary>
        public static string FormatUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified)
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
            return FormatUtc(new DateTimeOffset(dt));
        }

        /// <summary>Date-only ISO format "2026-05-20" for fields like estimatedArrivalAtDestinationDateOnly.</summary>
        public static string FormatDateOnly(DateTime dt) =>
            dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
