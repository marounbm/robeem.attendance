namespace robeem.Contracts.Attendance;

/// <summary>One employee in the compensation overview: their pay period and the totals of its report.</summary>
public sealed class CompensationOverviewItem
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string ScheduleName { get; init; } = string.Empty;

    public DateTime PeriodStart { get; init; }

    public DateTime PeriodEnd { get; init; }

    public bool PeriodHasEnded { get; init; }

    public bool IsClosed { get; init; }

    public int NotReadyDays { get; init; }

    public bool CanClose { get; init; }

    public CompensationSummary Summary { get; init; } = new();
}
