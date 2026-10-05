namespace robeem.Contracts.Attendance;

/// <summary>
/// An employee's proposal to fix their own open attendance exception. The proposer is the
/// authenticated user, who must be the exception's employee.
/// </summary>
public sealed class ProposeCorrectionRequest
{
    public int ExceptionId { get; init; }

    /// <summary>Gets when the missing punch happened, as local wall-clock time.</summary>
    public DateTime ProposedValue { get; init; }

    public string Justification { get; init; } = string.Empty;
}
