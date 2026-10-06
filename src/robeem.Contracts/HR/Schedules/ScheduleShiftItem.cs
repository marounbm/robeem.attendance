namespace robeem.Contracts.HR.Schedules;

/// <summary>A shift of a time schedule, with what follows from its times.</summary>
public sealed class ScheduleShiftItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string StartTime { get; init; } = string.Empty;

    public string EndTime { get; init; } = string.Empty;

    public bool BreakTime { get; init; }

    public string BreakStartTime { get; init; } = string.Empty;

    public string BreakEndTime { get; init; } = string.Empty;

    public bool Overtime { get; init; }

    /// <summary>Gets the paid hours: the shift's length minus its break.</summary>
    public double TotalHours { get; init; }

    /// <summary>Gets a value indicating whether the shift ends the next day.</summary>
    public bool IsOvernight { get; init; }

    /// <summary>Gets the number of roster entries that use the shift. A shift in use can't be deleted.</summary>
    public int RosterUses { get; init; }
}
