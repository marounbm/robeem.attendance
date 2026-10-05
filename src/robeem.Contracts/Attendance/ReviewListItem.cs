namespace robeem.Contracts.Attendance;

/// <summary>An attendance record waiting for a reviewer's decision.</summary>
public sealed class ReviewListItem
{
    public int RecordId { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    public DateTime? ScheduledStart { get; init; }

    public DateTime? ScheduledEnd { get; init; }

    public DateTime? ActualCheckIn { get; init; }

    public DateTime? ActualCheckOut { get; init; }

    public TimeSpan WorkingHours { get; init; }

    public int OvertimeMinutes { get; init; }

    /// <summary>Gets why the record needs review: Overtime, UnscheduledWork or IncompleteDay (the first that applies), otherwise Review.</summary>
    public string Reason { get; init; } = string.Empty;
}
