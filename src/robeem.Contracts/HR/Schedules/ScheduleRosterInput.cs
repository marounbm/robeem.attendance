namespace robeem.Contracts.HR.Schedules;

/// <summary>The whole roster of one team, replacing what it had.</summary>
public sealed class ScheduleRosterInput
{
    public List<ScheduleRosterCell> Cells { get; set; } = [];
}
