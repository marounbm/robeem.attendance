using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>The outcome of a reset, with what was deleted when it was done.</summary>
public sealed record AttendanceDataResetResult(AttendanceDataResetOutcome Outcome, IReadOnlyList<AttendanceDataCount> Deleted)
{
    public static AttendanceDataResetResult Refused(AttendanceDataResetOutcome outcome) => new(outcome, []);
}
