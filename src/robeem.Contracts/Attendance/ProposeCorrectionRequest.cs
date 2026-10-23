namespace robeem.Contracts.Attendance;

/// <summary>
/// An employee's proposal to fix their own open attendance exception. The proposer is the
/// authenticated user, who must be the exception's employee. For a missing punch the proposal is the time
/// the punch happened; for punches in the wrong order it is the type the out-of-order punch should have.
/// </summary>
public sealed class ProposeCorrectionRequest
{
    public int ExceptionId { get; init; }

    /// <summary>Gets when the missing punch happened, as local wall-clock time. Not used when <see cref="NewEventType"/> is given.</summary>
    public DateTime ProposedValue { get; init; }

    /// <summary>
    /// Gets the type to give the out-of-order punch of an invalid-sequence exception: one of the exception's suggested types
    /// (CheckIn, CheckOut, BreakOut or BreakIn). Null for a missing punch.
    /// </summary>
    public string? NewEventType { get; init; }

    public string Justification { get; init; } = string.Empty;
}
