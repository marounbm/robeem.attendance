using System.Globalization;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// How a ZKTeco attendance export writes timestamps and punch status.
/// </summary>
/// <remarks>
/// <para>Timestamps are wall-clock times at the device, with no offset, so the device's time zone
/// applies. The machine writes <c>01-Apr-26 08:33:25</c> (day first, English month); a file that was
/// opened and saved as CSV in Excel often has <c>01/04/2026 08:33</c> or an ISO form instead. All of
/// these are day-first, which is the one reading this vendor uses; see <see cref="TimestampFormats"/>.</para>
/// <para>Status is matched ignoring case, spaces and punctuation, so <c>C/In</c> and <c>c in</c> are
/// the same. <c>OT In</c> and <c>OT Out</c> are overtime punches: they become
/// <see cref="NormalizedEventType.Other"/>, which the calculation ignores, rather than being guessed
/// as a check-in or check-out. See <see cref="EventCodes"/>.</para>
/// </remarks>
public sealed class ZktecoInterpreter : IAttendanceEventInterpreter
{
    /// <summary>The format the clock's software writes, as in <c>01-Apr-26 08:33:25</c>; the one the layout shown to users lists.</summary>
    public const string ExportTimestampFormat = "dd-MMM-yy HH:mm:ss";

    /// <summary>The formats accepted, in the order they are tried.</summary>
    public static readonly IReadOnlyList<string> TimestampFormats =
    [
        "d-MMM-yy H:mm:ss",
        "d-MMM-yy H:mm",
        "d-MMM-yyyy H:mm:ss",
        "d-MMM-yyyy H:mm",
        "d/M/yyyy H:mm:ss",
        "d/M/yyyy H:mm",
        "d/M/yy H:mm:ss",
        "d/M/yy H:mm",
        "yyyy-MM-dd H:mm:ss",
        "yyyy-MM-dd H:mm",
    ];

    /// <summary>The status labels the vendor writes and what each becomes.</summary>
    public static readonly IReadOnlyList<(string Code, NormalizedEventType Type)> EventCodes =
    [
        ("C/In", NormalizedEventType.CheckIn),
        ("C/Out", NormalizedEventType.CheckOut),
        ("Break Out", NormalizedEventType.BreakOut),
        ("Break In", NormalizedEventType.BreakIn),
        ("OT In", NormalizedEventType.Other),
        ("OT Out", NormalizedEventType.Other),
    ];

    /// <summary>The labels the shared export contains (the others in <see cref="EventCodes"/> are read when a clock writes them).</summary>
    public static readonly IReadOnlyList<string> ExportEventCodes = ["C/In", "C/Out"];

    private static readonly Dictionary<string, NormalizedEventType> EventTypes =
        EventCodes.ToDictionary(c => Key(c.Code), c => c.Type, StringComparer.Ordinal);

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw)
    {
        var text = transactionTimestampRaw.Trim();
        return DateTime.TryParseExact(text, TimestampFormats.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? new VendorTimestamp(wallClock, null)
            : null;
    }

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) =>
        Map(transactionTypeRaw) ?? Map(directionRaw);

    private static string Key(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static NormalizedEventType? Map(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return EventTypes.TryGetValue(Key(value), out var type) ? type : null;
    }
}
