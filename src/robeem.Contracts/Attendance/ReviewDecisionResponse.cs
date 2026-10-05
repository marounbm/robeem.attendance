namespace robeem.Contracts.Attendance;

public sealed class ReviewDecisionResponse
{
    public int DecisionId { get; init; }

    public int RecordId { get; init; }

    /// <summary>Gets the decision: Approved or Rejected.</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Gets the record's status now: Approved or Rejected.</summary>
    public string RecordStatus { get; init; } = string.Empty;
}
