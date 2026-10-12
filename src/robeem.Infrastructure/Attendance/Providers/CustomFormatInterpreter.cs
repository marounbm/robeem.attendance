using System.Globalization;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// Interprets the rows of an administrator-defined file format: its timestamp patterns, and its
/// type labels. A timestamp may also end in <c>Z</c> or an offset such as <c>+03:00</c>; without
/// one it is wall-clock at the device. Labels are matched ignoring case, spaces and punctuation, the
/// type column first, then the direction column.
/// </summary>
public sealed class CustomFormatInterpreter : IAttendanceEventInterpreter
{
    private readonly string[] _wallClockFormats;
    private readonly string[] _offsetFormats;
    private readonly Dictionary<string, NormalizedEventType> _eventTypes;

    public CustomFormatInterpreter(AttendanceFileFormat format)
    {
        _wallClockFormats = FileFormatLayout.Patterns(format.TimestampFormats).ToArray();
        _offsetFormats = _wallClockFormats.SelectMany(f => new[] { f + "zzz", f + "'Z'" }).ToArray();
        _eventTypes = format.EventCodes.ToDictionary(c => FileFormatLayout.CodeKey(c.Code), c => c.EventType, StringComparer.Ordinal);
    }

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw)
    {
        var text = transactionTimestampRaw.Trim();
        if (DateTimeOffset.TryParseExact(text, _offsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withOffset))
        {
            return new VendorTimestamp(withOffset.DateTime, withOffset.Offset);
        }

        return DateTime.TryParseExact(text, _wallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? new VendorTimestamp(wallClock, null)
            : null;
    }

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) =>
        Map(transactionTypeRaw) ?? Map(directionRaw);

    private NormalizedEventType? Map(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _eventTypes.TryGetValue(FileFormatLayout.CodeKey(value), out var type) ? type : null;
}
