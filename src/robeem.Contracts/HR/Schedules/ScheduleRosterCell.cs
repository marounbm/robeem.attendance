namespace robeem.Contracts.HR.Schedules;

/// <summary>One shift worked on one day of the pattern. A day can have several (a split shift); a day with none is a day off.</summary>
public sealed class ScheduleRosterCell
{
    /// <summary>Gets or sets the day of the pattern, from 1 to its length.</summary>
    public int CycleDay { get; set; }

    public int ShiftId { get; set; }
}
