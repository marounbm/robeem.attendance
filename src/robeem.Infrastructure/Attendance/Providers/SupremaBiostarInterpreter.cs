using System.Globalization;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// How the BioStar 2 Local API writes event times and which T&amp;A keys it uses.
/// </summary>
/// <remarks>
/// <para>An event's <c>datetime</c> is an ISO 8601 UTC instant such as <c>2026-04-01T05:33:25.00Z</c>.
/// An offset such as <c>+03:00</c> is honoured. A time with neither is read as wall-clock at the
/// device, as the other adapters do; nothing else is guessed.</para>
/// <para>The punch type is the event's <c>tna_key</c>, set by the time-and-attendance key the person
/// pressed on the device (see <see cref="EventCodes"/>). The adapter sends it as the transaction's
/// type. An event without a T&amp;A key, such as a door opening, has no type: it is an unlabeled
/// punch, and what happens to it is the tenant's attendance policy to decide.</para>
/// </remarks>
public sealed class SupremaBiostarInterpreter : IAttendanceEventInterpreter
{
    /// <summary>The T&amp;A keys, their BioStar names, and what each becomes. Meal start and end are breaks.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, NormalizedEventType Type)> EventCodes =
    [
        ("1", "PUNCH_TYPE_CHECK_IN", NormalizedEventType.CheckIn),
        ("2", "PUNCH_TYPE_CHECK_OUT", NormalizedEventType.CheckOut),
        ("3", "PUNCH_TYPE_BREAK_START", NormalizedEventType.BreakOut),
        ("4", "PUNCH_TYPE_BREAK_END", NormalizedEventType.BreakIn),
        ("5", "PUNCH_TYPE_MEAL_START", NormalizedEventType.BreakOut),
        ("6", "PUNCH_TYPE_MEAL_END", NormalizedEventType.BreakIn),
    ];

    /// <summary>The timestamp patterns accepted, each with <c>Z</c> or an offset such as <c>+03:00</c> after it, or with nothing (wall-clock at the device).</summary>
    public static readonly IReadOnlyList<string> TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
    ];

    // Explicit 'Z' and zzz, not K: K reads a time with no offset in the machine's own zone.
    private static readonly string[] OffsetFormats = TimestampFormats.SelectMany(f => new[] { f + "'Z'", f + "zzz" }).ToArray();

    private static readonly Dictionary<string, NormalizedEventType> EventTypes =
        EventCodes.ToDictionary(c => c.Key, c => c.Type, StringComparer.Ordinal);

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw)
    {
        var text = transactionTimestampRaw.Trim();
        if (DateTimeOffset.TryParseExact(text, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withOffset))
        {
            return new VendorTimestamp(withOffset.DateTime, withOffset.Offset);
        }

        return DateTime.TryParseExact(text, TimestampFormats.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? new VendorTimestamp(wallClock, null)
            : null;
    }

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) =>
        transactionTypeRaw is { } key && EventTypes.TryGetValue(key.Trim(), out var type) ? type : null;
}
