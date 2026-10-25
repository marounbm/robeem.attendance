using System.Globalization;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Interprets the rows of an administrator-defined file format: its timestamp patterns, and its
/// type labels. A timestamp may also end in <c>Z</c> or an offset such as <c>+03:00</c>; without
/// one it is wall-clock at the device. Labels are matched ignoring case, spaces and punctuation, the
/// type column first, then the direction column. Month and day names are read in the format's culture
/// (invariant when it names none), always with the Gregorian calendar.
/// </summary>
public sealed class CustomFormatInterpreter : IAttendanceEventInterpreter, ITimestampExplainer
{
    private const string ReadingFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string[] _wallClockFormats;
    private readonly string[] _offsetFormats;
    private readonly CultureInfo _culture;
    private readonly Dictionary<string, NormalizedEventType> _eventTypes;

    public CustomFormatInterpreter(AttendancePayloadFormat format)
    {
        _wallClockFormats = PayloadFormatLayout.EffectivePatterns(format).ToArray();
        _offsetFormats = _wallClockFormats.SelectMany(f => new[] { f + "zzz", f + "'Z'" }).ToArray();
        _culture = TimestampCultures.Resolve(format.TimestampCulture);
        _eventTypes = format.EventCodes.ToDictionary(c => PayloadFormatLayout.CodeKey(c.Code), c => c.EventType, StringComparer.Ordinal);
    }

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw)
    {
        var text = transactionTimestampRaw.Trim();
        if (DateTimeOffset.TryParseExact(text, _offsetFormats, _culture, DateTimeStyles.AssumeUniversal, out var withOffset))
        {
            return new VendorTimestamp(withOffset.DateTime, withOffset.Offset);
        }

        return DateTime.TryParseExact(text, _wallClockFormats, _culture, DateTimeStyles.None, out var wallClock)
            ? new VendorTimestamp(wallClock, null)
            : null;
    }

    public IReadOnlyList<TimestampMatch> Explain(string timestampText)
    {
        var text = timestampText.Trim();
        var matches = new List<TimestampMatch>();
        foreach (var pattern in _wallClockFormats)
        {
            foreach (var offsetPattern in new[] { pattern + "zzz", pattern + "'Z'" })
            {
                if (DateTimeOffset.TryParseExact(text, offsetPattern, _culture, DateTimeStyles.AssumeUniversal, out var withOffset))
                {
                    matches.Add(new TimestampMatch(pattern, withOffset.DateTime.ToString(ReadingFormat, CultureInfo.InvariantCulture) + " " + OffsetText(withOffset.Offset)));
                }
            }

            if (DateTime.TryParseExact(text, pattern, _culture, DateTimeStyles.None, out var wallClock))
            {
                matches.Add(new TimestampMatch(pattern, wallClock.ToString(ReadingFormat, CultureInfo.InvariantCulture)));
            }
        }

        return matches;
    }

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) =>
        Map(transactionTypeRaw) ?? Map(directionRaw);

    private static string OffsetText(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    private NormalizedEventType? Map(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _eventTypes.TryGetValue(PayloadFormatLayout.CodeKey(value), out var type) ? type : null;
}
