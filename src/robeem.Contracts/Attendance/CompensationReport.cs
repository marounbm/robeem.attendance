namespace robeem.Contracts.Attendance;

/// <summary>One employee's compensation report for one pay period (their own, from their pay cycle).</summary>
public sealed class CompensationReport
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>Gets the employee's time schedule; empty when they have none.</summary>
    public string ScheduleName { get; init; } = string.Empty;

    /// <summary>Gets the pay cycle in words, for example "Monthly, from the 26th".</summary>
    public string CycleDescription { get; init; } = string.Empty;

    public DateTime PeriodStart { get; init; }

    /// <summary>Gets the last day of the period, inclusive.</summary>
    public DateTime PeriodEnd { get; init; }

    public bool PeriodHasEnded { get; init; }

    /// <summary>Gets a value indicating whether this employee's period is closed: payroll reads none of its days and the decisions are locked.</summary>
    public bool IsClosed { get; init; }

    public IReadOnlyList<CompensationLine> Lines { get; init; } = Array.Empty<CompensationLine>();

    public CompensationSummary Summary { get; init; } = new();

    /// <summary>
    /// Gets the days of the period that have attendance data but that payroll may not pay for yet: an unsettled exception, a day still to
    /// review, an unapproved day. The period cannot be closed while there are any.
    /// </summary>
    public IReadOnlyList<DateTime> NotReadyDays { get; init; } = Array.Empty<DateTime>();

    public bool CanClose { get; init; }

    /// <summary>Gets why the period cannot be closed now; null when it can, or when it already is.</summary>
    public string? CannotCloseReason { get; init; }
}
