namespace robeem.Contracts.Attendance;

/// <summary>Everything about one employee's day: the rollup, every record version, the exceptions, the corrections and the punches.</summary>
public sealed class RecordDayDetail
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets a value indicating whether the day was processed; when not, there is nothing below to show.</summary>
    public bool Processed { get; init; }

    public TimeSpan TotalWorkingHours { get; init; }

    public TimeSpan TotalOvertimeHours { get; init; }

    public TimeSpan TotalBreakHours { get; init; }

    public int ExceptionCount { get; init; }

    public string Status { get; init; } = string.Empty;

    /// <summary>Gets every version of every record of the day, current ones first within a shift.</summary>
    public IReadOnlyList<RecordVersionItem> Versions { get; init; } = Array.Empty<RecordVersionItem>();

    public IReadOnlyList<ExceptionListItem> Exceptions { get; init; } = Array.Empty<ExceptionListItem>();

    public IReadOnlyList<RecordCorrectionItem> Corrections { get; init; } = Array.Empty<RecordCorrectionItem>();

    public IReadOnlyList<RecordPunchItem> Punches { get; init; } = Array.Empty<RecordPunchItem>();
}
