namespace robeem.Contracts.Attendance;

/// <summary>
/// Decisions on lines of one employee's compensation report. The decider is the authenticated payroll administrator, never a field of
/// the request. Nothing is saved when any line is refused.
/// </summary>
public sealed class CompensationDecisionRequest
{
    /// <summary>The decisions, as <c>CompensationDecisionKind</c> names them.</summary>
    public static readonly IReadOnlyList<string> Decisions = ["Ignore", "Overtime", "PaidLeave", "UnpaidLeave", "Penalise"];

    /// <summary>The leave types a paid-leave decision can add (an unpaid-leave decision always adds Unpaid).</summary>
    public static readonly IReadOnlyList<string> PaidLeaveTypes = ["Annual", "Sick", "Other"];

    public int EmployeeId { get; init; }

    /// <summary>Gets the first day of the pay period the lines belong to.</summary>
    public DateTime PeriodStart { get; init; }

    public IReadOnlyList<CompensationLineDecision> Lines { get; init; } = Array.Empty<CompensationLineDecision>();

    /// <summary>
    /// Gets a decision (Ignore or Penalise) to apply to every under time line of the period that has none yet, instead of listing them.
    /// Leave decisions add leaves to the employee's leaves, so they are never bulk.
    /// </summary>
    public string? UndecidedUndertime { get; init; }
}
