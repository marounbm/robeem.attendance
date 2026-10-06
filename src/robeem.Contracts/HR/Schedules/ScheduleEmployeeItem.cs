namespace robeem.Contracts.HR.Schedules;

/// <summary>An employee assigned to a time schedule, and the team they are in.</summary>
public sealed class ScheduleEmployeeItem
{
    public int EmployeeId { get; init; }

    public string EmployeeNumber { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int TeamId { get; init; }
}
