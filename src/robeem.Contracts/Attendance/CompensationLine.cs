namespace robeem.Contracts.Attendance;

/// <summary>
/// One line of an employee's compensation report: a day's overtime or under time, to decide, or a day whose late or early minutes only
/// weigh in the penalties. Lines are in date order.
/// </summary>
public sealed class CompensationLine
{
    /// <summary>The line is a day's overtime.</summary>
    public const string Overtime = "Overtime";

    /// <summary>The line is a day's under time, or under time already covered by a leave.</summary>
    public const string Undertime = "Undertime";

    /// <summary>The line only carries penalty minutes (late or early minutes on a day with no under time or overtime). Nothing to decide.</summary>
    public const string Penalty = "Penalty";

    /// <summary>The kinds of overtime, shown with an overtime line.</summary>
    public static readonly IReadOnlyList<string> OvertimeKinds = ["Weekday", "DayOff", "Holiday"];

    public DateTime Date { get; init; }

    /// <summary>Gets what the line is about: <see cref="Overtime"/>, <see cref="Undertime"/> or <see cref="Penalty"/>.</summary>
    public string LineKind { get; init; } = string.Empty;

    /// <summary>Gets what was scheduled, for example "09:00–17:00", or "Day off" or "Public holiday".</summary>
    public string Scheduled { get; init; } = string.Empty;

    /// <summary>Gets the kind of overtime (Weekday, DayOff or Holiday); empty for any other line.</summary>
    public string OvertimeKind { get; init; } = string.Empty;

    public int WorkedMinutes { get; init; }

    /// <summary>Gets the minutes of the line: the overtime or the under time (the part not yet covered by a leave).</summary>
    public int Minutes { get; init; }

    public int LateMinutes { get; init; }

    public int EarlyMinutes { get; init; }

    /// <summary>Gets the penalty minutes of this line: late and early minutes weighted by the schedule's percents, and the under time payroll decided to penalise.</summary>
    public int PenaltyMinutes { get; init; }

    /// <summary>Gets the penalty minutes accumulated up to and including this line.</summary>
    public int RunningPenaltyMinutes { get; init; }

    /// <summary>Gets the decision in force (Ignore, Overtime, PaidLeave, UnpaidLeave or Penalise); null when none.</summary>
    public string? Decision { get; init; }

    /// <summary>Gets the minutes the decision was made on.</summary>
    public int DecidedMinutes { get; init; }

    public string? DecidedByName { get; init; }

    public DateTime? DecidedAt { get; init; }

    /// <summary>Gets a value indicating whether the line waits for a decision: there is none, or it was made on other minutes (the day was calculated again).</summary>
    public bool NeedsDecision { get; init; }

    /// <summary>Gets a value indicating whether this under time was covered by a leave added to the employee's leaves.</summary>
    public bool IsCoveredByLeave { get; init; }

    /// <summary>Gets the decisions payroll may take on the line now; empty once it is covered by leave, or when the period is closed.</summary>
    public IReadOnlyList<string> AllowedDecisions { get; init; } = Array.Empty<string>();

    /// <summary>Gets the minutes a leave could cover (the time missed at the start and end of the shift); 0 when none can.</summary>
    public int LeavePlaceableMinutes { get; init; }
}
