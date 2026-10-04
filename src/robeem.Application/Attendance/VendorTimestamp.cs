namespace robeem.Application.Attendance;

/// <summary>
/// A vendor timestamp, parsed from the vendor's format by its adapter. <see cref="Offset"/> is null
/// when the vendor sent a wall-clock time only; the device's time zone then applies.
/// </summary>
public readonly record struct VendorTimestamp(DateTime WallClock, TimeSpan? Offset)
{
    /// <summary>
    /// Gets the instant in UTC: from the vendor's offset when there is one, otherwise from the IANA
    /// <paramref name="timeZoneId"/>. Null when neither is known, or the wall-clock time does not
    /// exist in that zone (a daylight-saving gap).
    /// </summary>
    public DateTimeOffset? ToUtc(string? timeZoneId)
    {
        var wallClock = DateTime.SpecifyKind(WallClock, DateTimeKind.Unspecified);
        if (Offset is { } offset)
        {
            return new DateTimeOffset(wallClock, offset).ToUniversalTime();
        }

        if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone) || timeZone.IsInvalidTime(wallClock))
        {
            return null;
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wallClock, timeZone));
    }
}
