namespace robeem.Contracts.Attendance;

/// <summary>
/// The totals of an employee's compensation report, in minutes: before the decisions, the decisions, and after them.
/// <see cref="BalanceMinutes"/> is the overtime counted minus the under time that remains, and the report ends with one overtime line.
/// </summary>
public sealed class CompensationSummary
{
    /// <summary>Gets the days of the period payroll may pay for (approved and exception-free).</summary>
    public int PaidDays { get; init; }

    public int WorkedMinutes { get; init; }

    /// <summary>Gets the worked minutes that are not an overtime line.</summary>
    public int RegularMinutes { get; init; }

    // Before the decisions.
    public int OvertimeMinutes { get; init; }

    public int UndertimeMinutes { get; init; }

    // The decisions.
    public int OvertimeIgnoredMinutes { get; init; }

    public int OvertimeAcceptedMinutes { get; init; }

    public int UndertimeIgnoredMinutes { get; init; }

    public int UndertimePaidLeaveMinutes { get; init; }

    public int UndertimeUnpaidLeaveMinutes { get; init; }

    public int UndertimePenalisedMinutes { get; init; }

    // After the decisions.

    /// <summary>Gets the overtime that counts: all of it except what was ignored (overtime nobody decided counts by default).</summary>
    public int OvertimeCountedMinutes { get; init; }

    /// <summary>Gets the under time that is left: not ignored, not covered by leave, not penalised. It offsets overtime.</summary>
    public int UndertimeRemainingMinutes { get; init; }

    /// <summary>Gets overtime counted minus under time remaining; negative is a deficit.</summary>
    public int BalanceMinutes { get; init; }

    /// <summary>Gets the one overtime line the report ends with: the balance when it is positive, else 0.</summary>
    public int OvertimeLineMinutes { get; init; }

    /// <summary>Gets the under time left over when the balance is negative, else 0.</summary>
    public int DeficitMinutes { get; init; }

    /// <summary>Gets the penalty minutes accumulated over the period.</summary>
    public int PenaltyMinutes { get; init; }

    /// <summary>Gets the number of lines still waiting for a decision.</summary>
    public int UndecidedLines { get; init; }
}
