namespace robeem.Contracts.Attendance;

/// <summary>
/// One day of an employee's pay period that payroll may pay for, with what the report made of it: the hours, the overtime or under time it gave a
/// line, the penalty minutes, and the decisions on it. It is the day-by-day detail behind the totals of the compensation report, and is still
/// there once the period is closed.
/// </summary>
public sealed class CompensationDay
{
    public DateTime Date { get; init; }

    /// <summary>Gets what was scheduled, for example "08:00–17:00", or "Day off" or "Public holiday".</summary>
    public string Scheduled { get; init; } = string.Empty;

    /// <summary>Gets the shifts as punched, for example "08:00–17:00"; a check-out on a later date says so ("22:28–06:31 +1 day").</summary>
    public string Worked { get; init; } = string.Empty;

    public int WorkedMinutes { get; init; }

    public int BreakMinutes { get; init; }

    /// <summary>Gets the kind of overtime of the day (Weekday, DayOff or Holiday); empty when it has none.</summary>
    public string OvertimeKind { get; init; } = string.Empty;

    public int OvertimeMinutes { get; init; }

    /// <summary>Gets the under time of the day that is still under time (not covered by a leave).</summary>
    public int UndertimeMinutes { get; init; }

    /// <summary>Gets the under time a leave added from the report covered.</summary>
    public int CoveredByLeaveMinutes { get; init; }

    public int LateMinutes { get; init; }

    public int EarlyMinutes { get; init; }

    public int PenaltyMinutes { get; init; }

    /// <summary>Gets what was decided about the day's lines, for example "Overtime: Ignore; Under time: Penalise"; what is not decided says what it does.</summary>
    public string Decisions { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a punch of the day was read as another type by a rule.</summary>
    public bool HasInferredPunches { get; init; }
}
