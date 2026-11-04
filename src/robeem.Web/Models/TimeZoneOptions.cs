namespace robeem.Web.Models;

/// <summary>The IANA time zones the forms offer in a drop-down.</summary>
public static class TimeZoneOptions
{
    /// <summary>Gets the zones offered, by region; any IANA zone id is accepted by the Api.</summary>
    public static IReadOnlyList<string> Common { get; } =
    [
        "UTC",
        "Africa/Abidjan", "Africa/Accra", "Africa/Addis_Ababa", "Africa/Algiers", "Africa/Cairo", "Africa/Casablanca", "Africa/Dar_es_Salaam",
        "Africa/Johannesburg", "Africa/Khartoum", "Africa/Lagos", "Africa/Nairobi", "Africa/Tripoli", "Africa/Tunis",
        "America/Anchorage", "America/Argentina/Buenos_Aires", "America/Bogota", "America/Caracas", "America/Chicago", "America/Denver",
        "America/Halifax", "America/Lima", "America/Los_Angeles", "America/Mexico_City", "America/New_York", "America/Phoenix",
        "America/Santiago", "America/Sao_Paulo", "America/Toronto", "America/Vancouver",
        "Asia/Amman", "Asia/Baghdad", "Asia/Bahrain", "Asia/Baku", "Asia/Bangkok", "Asia/Beirut", "Asia/Colombo", "Asia/Damascus",
        "Asia/Dhaka", "Asia/Dubai", "Asia/Gaza", "Asia/Ho_Chi_Minh", "Asia/Hong_Kong", "Asia/Jakarta", "Asia/Jerusalem", "Asia/Kabul",
        "Asia/Karachi", "Asia/Kathmandu", "Asia/Kolkata", "Asia/Kuala_Lumpur", "Asia/Kuwait", "Asia/Manila", "Asia/Muscat", "Asia/Qatar",
        "Asia/Riyadh", "Asia/Seoul", "Asia/Shanghai", "Asia/Singapore", "Asia/Taipei", "Asia/Tashkent", "Asia/Tbilisi", "Asia/Tehran",
        "Asia/Tokyo", "Asia/Yerevan",
        "Atlantic/Reykjavik",
        "Australia/Adelaide", "Australia/Brisbane", "Australia/Darwin", "Australia/Melbourne", "Australia/Perth", "Australia/Sydney",
        "Europe/Amsterdam", "Europe/Athens", "Europe/Berlin", "Europe/Brussels", "Europe/Bucharest", "Europe/Budapest", "Europe/Dublin",
        "Europe/Helsinki", "Europe/Istanbul", "Europe/Kyiv", "Europe/Lisbon", "Europe/London", "Europe/Madrid", "Europe/Moscow",
        "Europe/Nicosia", "Europe/Oslo", "Europe/Paris", "Europe/Prague", "Europe/Rome", "Europe/Stockholm", "Europe/Vienna",
        "Europe/Warsaw", "Europe/Zurich",
        "Indian/Maldives", "Indian/Mauritius",
        "Pacific/Auckland", "Pacific/Fiji", "Pacific/Honolulu",
    ];

    /// <summary>The zones for a drop-down: the common ones, and the zone already chosen when it is not among them, so an existing value is never lost.</summary>
    public static IReadOnlyList<string> Choices(string? current) =>
        string.IsNullOrWhiteSpace(current) || Common.Contains(current, StringComparer.Ordinal)
            ? Common
            : [current.Trim(), .. Common];
}
