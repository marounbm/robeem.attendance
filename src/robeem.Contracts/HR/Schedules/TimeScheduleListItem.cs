namespace robeem.Contracts.HR.Schedules;

/// <summary>A time schedule as the list shows it.</summary>
public sealed class TimeScheduleListItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Gets the number of days after which the pattern repeats: 7 for a weekly pattern.</summary>
    public int CyclePeriod { get; init; }

    public int ShiftCount { get; init; }

    public int TeamCount { get; init; }

    /// <summary>Gets the number of employees assigned to the schedule.</summary>
    public int EmployeeCount { get; init; }

    /// <summary>Gets a value indicating whether the system created and owns the schedule: it can't be deleted.</summary>
    public bool IsSystemSchedule { get; init; }
}
