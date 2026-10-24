using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>What a user may do with an exception, in one place for every screen that lists them.</summary>
internal static class AttendanceExceptionRules
{
    /// <summary>
    /// Whether the viewer can propose a correction now: it is their own and open, and either a punch is missing (they say when it happened)
    /// or a punch is out of order and at least one other type would settle it.
    /// </summary>
    public static bool CanPropose(AttendanceExceptionType type, AttendanceExceptionStatus status, int employeeId, int viewerId, string suggestedEventTypes) =>
        employeeId == viewerId
        && status == AttendanceExceptionStatus.Open
        && (type == AttendanceExceptionType.MissingPunch
            || (type == AttendanceExceptionType.InvalidSequence && suggestedEventTypes.Length > 0));

    /// <summary>Whether the viewer can correct the day of the exception: it is their own and still open, whatever it is about.</summary>
    public static bool CanCorrect(AttendanceExceptionStatus status, int employeeId, int viewerId) =>
        employeeId == viewerId && status == AttendanceExceptionStatus.Open;

    /// <summary>Splits the stored list of suggested event types.</summary>
    public static IReadOnlyList<string> Suggested(string suggestedEventTypes) =>
        suggestedEventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
