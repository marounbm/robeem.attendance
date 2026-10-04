namespace robeem.Contracts.Attendance;

/// <summary>Where a correction and its exception stand after a decision.</summary>
public sealed class CorrectionDecisionResponse
{
    public int CorrectionId { get; init; }

    /// <summary>Gets the correction status: Pending, Rejected or Applied.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the exception status: Open, PendingCorrection or Closed.</summary>
    public string ExceptionStatus { get; init; } = string.Empty;
}
