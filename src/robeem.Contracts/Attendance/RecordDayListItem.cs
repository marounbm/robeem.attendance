namespace robeem.Contracts.Attendance;

/// <summary>One employee's day as the records list shows it: the rollup of that date's current records.</summary>
public sealed class RecordDayListItem
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    public TimeSpan TotalWorkingHours { get; init; }

    public TimeSpan TotalOvertimeHours { get; init; }

    public TimeSpan TotalBreakHours { get; init; }

    /// <summary>Gets the number of current records that day, unscheduled work included.</summary>
    public int ShiftCount { get; init; }

    /// <summary>Gets the number of exceptions that day that are not closed yet.</summary>
    public int ExceptionCount { get; init; }

    /// <summary>Gets the day's status: Calculated, PendingReview, Approved or Rejected.</summary>
    public string Status { get; init; } = string.Empty;
}
