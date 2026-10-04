using System.Globalization;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// How the generic CSV format writes timestamps and event types.
/// </summary>
/// <remarks>
/// <para>Timestamps are ISO 8601, <c>yyyy-MM-dd HH:mm[:ss[.fffffff]]</c> (a <c>T</c> separator is
/// also accepted), optionally followed by <c>Z</c> or an offset such as <c>+03:00</c>. Other formats
/// are not guessed, since <c>04/10/2026</c> is ambiguous.</para>
/// <para>Event types are matched ignoring case, spaces and punctuation, so <c>Check-In</c> and
/// <c>BREAK, OUT</c> are recognized. The type column is tried first, then the direction column.
/// The recognized values are listed in <see cref="EventTypes"/>.</para>
/// </remarks>
public sealed class GenericCsvInterpreter : IAttendanceEventInterpreter
{
    private static readonly string[] WallClockFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm",
    ];

    private static readonly string[] OffsetFormats = WallClockFormats.SelectMany(f => new[] { f + "zzz", f + "'Z'" }).ToArray();

    private static readonly Dictionary<string, NormalizedEventType> EventTypes = new(StringComparer.Ordinal)
    {
        ["IN"] = NormalizedEventType.CheckIn,
        ["CHECKIN"] = NormalizedEventType.CheckIn,
        ["CLOCKIN"] = NormalizedEventType.CheckIn,
        ["OUT"] = NormalizedEventType.CheckOut,
        ["CHECKOUT"] = NormalizedEventType.CheckOut,
        ["CLOCKOUT"] = NormalizedEventType.CheckOut,
        ["BREAKOUT"] = NormalizedEventType.BreakOut,
        ["BREAKSTART"] = NormalizedEventType.BreakOut,
        ["BREAKIN"] = NormalizedEventType.BreakIn,
        ["BREAKEND"] = NormalizedEventType.BreakIn,
    };

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw)
    {
        var text = transactionTimestampRaw.Trim();
        if (DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withOffset))
        {
            return new VendorTimestamp(withOffset.DateTime, withOffset.Offset);
        }

        if (DateTime.TryParseExact(text, WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock))
        {
            return new VendorTimestamp(wallClock, null);
        }

        return null;
    }

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) =>
        Map(transactionTypeRaw) ?? Map(directionRaw);

    private static NormalizedEventType? Map(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var key = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return EventTypes.TryGetValue(key, out var type) ? type : null;
    }
}
