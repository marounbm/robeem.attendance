namespace robeem.Contracts.Attendance;

/// <summary>A day of the compensation report with the employee and period it belongs to: one line of the days export.</summary>
public sealed class CompensationDayRow
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string ScheduleName { get; init; } = string.Empty;

    public DateTime PeriodStart { get; init; }

    public DateTime PeriodEnd { get; init; }

    public bool PeriodIsClosed { get; init; }

    public CompensationDay Day { get; init; } = new();
}
