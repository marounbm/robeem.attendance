namespace robeem.Tests.Attendance.Scenarios;

/// <summary>
/// A shift as the scenario data knows it, written down here on purpose and not read from the database: the tests then compare what the
/// seeded schedules resolve to against it, instead of trusting the schedules to say what they are.
/// </summary>
internal sealed record ScenarioShift(string Start, string End, string? BreakStart = null, string? BreakEnd = null)
{
    public bool HasBreak => BreakStart is not null;

    public bool IsOvernight => Minutes(End) <= Minutes(Start);

    /// <summary>Gets the scheduled break's length in minutes; 0 for a shift without one.</summary>
    public int BreakMinutes => HasBreak ? Minutes(BreakEnd!) - Minutes(BreakStart!) : 0;

    /// <summary>Gets the scheduled working minutes: the shift's length without its break.</summary>
    public int WorkMinutes => (IsOvernight ? Minutes(End) + 1440 - Minutes(Start) : Minutes(End) - Minutes(Start)) - BreakMinutes;

    public DateTime StartsOn(DateTime day) => day.Date.AddMinutes(Minutes(Start));

    public DateTime EndsOn(DateTime day) => day.Date.AddMinutes(Minutes(End) + (IsOvernight ? 1440 : 0));

    public DateTime BreakStartsOn(DateTime day) => day.Date.AddMinutes(Minutes(BreakStart!));

    public DateTime BreakEndsOn(DateTime day) => day.Date.AddMinutes(Minutes(BreakEnd!));

    private static int Minutes(string time)
    {
        var parts = time.Split(':');
        return (int.Parse(parts[0]) * 60) + int.Parse(parts[1]);
    }
}
