namespace robeem.Contracts.Attendance;

/// <summary>A correction proposed for a missing punch of the day, and how far it got.</summary>
public sealed class RecordCorrectionItem
{
    public int Id { get; init; }

    /// <summary>Gets what it does: SupplyPunch or ChangePunchType.</summary>
    public string Kind { get; init; } = "SupplyPunch";

    /// <summary>Gets the type the punch had before a change of type; null for a supplied punch.</summary>
    public string? OriginalEventType { get; init; }

    /// <summary>Gets the punch it supplies, or the type the existing punch becomes: CheckIn, CheckOut, BreakOut or BreakIn.</summary>
    public string CorrectedEventType { get; init; } = string.Empty;

    public DateTime ProposedValue { get; init; }

    public string Justification { get; init; } = string.Empty;

    public string RequestedByName { get; init; } = string.Empty;

    public DateTime RequestedAt { get; init; }

    /// <summary>Gets who must approve: Manager, HR or Both.</summary>
    public string ApprovalWorkflow { get; init; } = string.Empty;

    /// <summary>Gets where it stands: Pending, Approved, Rejected or Applied.</summary>
    public string Status { get; init; } = string.Empty;

    public string? ManagerDecision { get; init; }

    public string? ManagerDecidedByName { get; init; }

    public string? HrDecision { get; init; }

    public string? HrDecidedByName { get; init; }

    public DateTime? AppliedAt { get; init; }
}
