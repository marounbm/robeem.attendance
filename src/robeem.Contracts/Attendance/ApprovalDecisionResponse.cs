namespace robeem.Contracts.Attendance;

/// <summary>Where an approval stands after a decision.</summary>
public sealed class ApprovalDecisionResponse
{
    public int ApprovalId { get; init; }

    /// <summary>Gets the kind: Correction or Review.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Gets the approval's status: Pending (a next step is waiting), Rejected, Approved (a review) or Applied (a correction).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the status of the exception a correction waited for: Open, PendingCorrection or Closed. Null for a review.</summary>
    public string? ExceptionStatus { get; init; }

    /// <summary>Gets the record's status now (Approved or Rejected). Null for a correction.</summary>
    public string? RecordStatus { get; init; }
}
