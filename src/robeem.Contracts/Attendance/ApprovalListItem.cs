namespace robeem.Contracts.Attendance;

/// <summary>
/// One thing waiting for an approver's decision: a correction request or a record left for review. The fields every kind has are
/// here; the details of the kind are <see cref="Correction"/> or <see cref="Review"/>.
/// </summary>
public sealed class ApprovalListItem
{
    /// <summary>Gets the approval's id, which the decision is posted to.</summary>
    public int Id { get; init; }

    /// <summary>Gets the kind: Correction or Review.</summary>
    public string Type { get; init; } = string.Empty;

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets when the correction was asked for, or when the review was queued.</summary>
    public DateTime RequestedAt { get; init; }

    /// <summary>Gets the step this user would be deciding: Manager or HR.</summary>
    public string AwaitingStep { get; init; } = string.Empty;

    /// <summary>Gets the details of a correction request; null for a review.</summary>
    public CorrectionListItem? Correction { get; init; }

    /// <summary>Gets the details of a record waiting for review; null for a correction.</summary>
    public ReviewListItem? Review { get; init; }
}
