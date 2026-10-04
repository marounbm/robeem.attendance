namespace robeem.Application.Attendance;

public enum ShiftResolutionOutcome
{
    /// <summary>The employee works one or more shifts that day.</summary>
    Scheduled,

    /// <summary>The employee's schedule has no line for that cycle day.</summary>
    DayOff,

    /// <summary>The employee is not assigned to any time schedule. Never guessed (CLAUDE.md §3.2).</summary>
    NoScheduleConfigured,
}
