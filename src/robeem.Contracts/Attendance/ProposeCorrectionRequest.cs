namespace robeem.Contracts.Attendance;

/// <summary>An employee's proposal to fix their own open attendance exception.</summary>
public sealed class ProposeCorrectionRequest
{
    public int ExceptionId { get; init; }

    /// <summary>Gets when the missing punch happened, as local wall-clock time.</summary>
    public DateTime ProposedValue { get; init; }

    public string Justification { get; init; } = string.Empty;

    /// <summary>
    /// Gets the employee who proposes. Must be the exception's employee.
    /// TODO(P12): take this from the authenticated user instead of the body; there is no
    /// authentication in this solution yet (CLAUDE.md §2.8).
    /// </summary>
    public int RequestedByEmployeeId { get; init; }
}
