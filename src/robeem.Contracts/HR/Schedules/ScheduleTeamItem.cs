namespace robeem.Contracts.HR.Schedules;

/// <summary>A team of a time schedule and its roster.</summary>
public sealed class ScheduleTeamItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the team contains everyone on the schedule (the default team) rather than chosen members.</summary>
    public bool AllInclusive { get; init; }

    public int EmployeeCount { get; init; }

    public IReadOnlyList<ScheduleRosterCell> Cells { get; init; } = Array.Empty<ScheduleRosterCell>();
}
