using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Infrastructure.Attendance.Providers;

/// <summary>
/// How the generic webhook format writes timestamps and event types: the same as the generic CSV
/// format (ISO 8601 with an optional <c>Z</c> or offset; <c>IN</c>, <c>OUT</c>, <c>BREAK OUT</c> and
/// their spellings), so the two share one reading rather than keeping two copies of it.
/// </summary>
public sealed class GenericWebhookInterpreter : IAttendanceEventInterpreter
{
    private static readonly GenericCsvInterpreter Shared = new();

    public VendorTimestamp? ParseTimestamp(string transactionTimestampRaw) => Shared.ParseTimestamp(transactionTimestampRaw);

    public NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw) => Shared.MapEventType(transactionTypeRaw, directionRaw);
}
