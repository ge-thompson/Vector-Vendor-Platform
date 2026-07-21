namespace Vector.VVI.Admin.Common.Time;

/// <summary>
/// Marks which storage convention a datetime column uses, so display code never
/// double-converts. UTC columns (CreatedUtc/ModifiedUtc) convert to Central for
/// display; stop times are already local wall-clock at the stop and show as-is.
/// </summary>
public enum TimeConvention
{
    /// <summary>Stored as real UTC. Convert to Central for display.</summary>
    Utc,

    /// <summary>Stored as local wall-clock at the stop location. Display as-is.</summary>
    LocalWallClock
}

/// <summary>
/// Central display of stored datetimes with a per-field convention tag.
/// </summary>
public static class TimeDisplay
{
    public const string DefaultFormat = "MM/dd/yyyy h:mm:ss tt";

    private static readonly TimeZoneInfo Central = ResolveCentral();

    private static TimeZoneInfo ResolveCentral()
    {
        // Windows id first (AWS box + Glen's workstation), IANA fallback for portability.
        foreach (var id in new[] { "Central Standard Time", "America/Chicago" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>Convert a stored UTC value to Central. Null-safe.</summary>
    public static DateTime? ToCentral(DateTime? utc)
    {
        if (utc is null) return null;
        var asUtc = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Central);
    }

    /// <summary>Format a value for display given its storage convention.</summary>
    public static string Show(DateTime? value, TimeConvention convention, string? format = null)
    {
        if (value is null) return string.Empty;
        format ??= DefaultFormat;
        var shown = convention == TimeConvention.Utc ? ToCentral(value) : value;
        return shown!.Value.ToString(format);
    }

    /// <summary>Tooltip text: the raw UTC value, for debugging double-conversion.</summary>
    public static string UtcTooltip(DateTime? utc, string? format = null)
        => utc is null ? string.Empty
                       : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)
                           .ToString(format ?? DefaultFormat) + " UTC";
}
