namespace robeem.Contracts.Attendance;

/// <summary>A reviewer's decision on one version of a record.</summary>
public sealed class RecordReviewItem
{
    /// <summary>Gets the outcome: Approved or Rejected.</summary>
    public string Outcome { get; init; } = string.Empty;

    public string DecidedByName { get; init; } = string.Empty;

    /// <summary>Gets the role the reviewer decided in: Manager or HR.</summary>
    public string DecidedByRole { get; init; } = string.Empty;

    public DateTime DecidedAt { get; init; }

    public string? Reason { get; init; }

    /// <summary>Gets the overtime the record had when it was decided: what the decision covered.</summary>
    public int OvertimeMinutes { get; init; }
}
