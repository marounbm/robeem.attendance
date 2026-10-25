using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The per-vendor half of normalization (CLAUDE.md §1.9): how one vendor writes timestamps and
/// event types. Registered as a keyed service under the vendor's <c>VendorKey</c>, next to its
/// <see cref="IAttendanceAdapter"/>. Vendor-specific knowledge lives here and in the adapter only.
/// </summary>
public interface IAttendanceEventInterpreter
{
    /// <summary>Parses a timestamp in the vendor's format; null when it does not parse.</summary>
    VendorTimestamp? ParseTimestamp(string transactionTimestampRaw);

    /// <summary>
    /// Maps the vendor's type and direction to an event type; null when the vendor sent a value
    /// this interpreter does not recognize. Not called for an unlabeled punch (both blank).
    /// </summary>
    NormalizedEventType? MapEventType(string? transactionTypeRaw, string? directionRaw);
}
