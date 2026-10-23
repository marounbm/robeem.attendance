namespace robeem.Contracts.Attendance;

public sealed class CorrectionListItem
{
    public int Id { get; init; }

    public int ExceptionId { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets what it does: SupplyPunch (adds the missing punch) or ChangePunchType (gives an existing punch another type).</summary>
    public string Kind { get; init; } = "SupplyPunch";

    /// <summary>Gets the type the punch had before a change of type: CheckIn, CheckOut, BreakOut or BreakIn; null for a supplied punch.</summary>
    public string? OriginalEventType { get; init; }

    /// <summary>Gets the punch the correction supplies, or the type the existing punch becomes: CheckIn, CheckOut, BreakOut or BreakIn.</summary>
    public string CorrectedEventType { get; init; } = string.Empty;

    /// <summary>Gets when the punch happened (a supplied punch), or the time of the punch whose type changes.</summary>
    public DateTime ProposedValue { get; init; }

    public string Justification { get; init; } = string.Empty;

    public DateTime RequestedAt { get; init; }

    /// <summary>Gets who must approve: Manager, HR or Both.</summary>
    public string ApprovalWorkflow { get; init; } = string.Empty;

    /// <summary>Gets the manager's decision, Approved or Rejected; null while undecided.</summary>
    public string? ManagerDecision { get; init; }
}
