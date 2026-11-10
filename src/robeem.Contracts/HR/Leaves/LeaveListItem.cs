namespace robeem.Contracts.HR.Leaves;

/// <summary>One leave as listed.</summary>
public sealed class LeaveListItem
{
    public int Id { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string LeaveType { get; init; } = string.Empty;

    public DateTime StartDate { get; init; }

    public DateTime EndDate { get; init; }

    public bool IsPartDay { get; init; }

    public string StartTime { get; init; } = string.Empty;

    public string EndTime { get; init; } = string.Empty;

    public string? Notes { get; init; }
}
