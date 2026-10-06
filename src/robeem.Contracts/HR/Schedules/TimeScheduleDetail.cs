namespace robeem.Contracts.HR.Schedules;

/// <summary>A time schedule in full: its settings, shifts, teams with their rosters, and the employees on it.</summary>
public sealed class TimeScheduleDetail
{
    public int Id { get; init; }

    public TimeScheduleInput Settings { get; init; } = new();

    public bool IsSystemSchedule { get; init; }

    public IReadOnlyList<ScheduleShiftItem> Shifts { get; init; } = Array.Empty<ScheduleShiftItem>();

    public IReadOnlyList<ScheduleTeamItem> Teams { get; init; } = Array.Empty<ScheduleTeamItem>();

    public IReadOnlyList<ScheduleEmployeeItem> Employees { get; init; } = Array.Empty<ScheduleEmployeeItem>();
}
