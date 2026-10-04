namespace robeem.Contracts.Attendance;

public sealed class CorrectionListItem
{
    public int Id { get; init; }

    public int ExceptionId { get; init; }

    public int EmployeeId { get; init; }

    public string EmployeeName { get; init; } = string.Empty;

    public DateTime AttendanceDate { get; init; }

    /// <summary>Gets the punch the correction supplies: CheckIn, CheckOut, BreakOut or BreakIn.</summary>
    public string CorrectedEventType { get; init; } = string.Empty;

    public DateTime ProposedValue { get; init; }

    public string Justification { get; init; } = string.Empty;

    public DateTime RequestedAt { get; init; }

    /// <summary>Gets who must approve: Manager, HR or Both.</summary>
    public string ApprovalWorkflow { get; init; } = string.Empty;

    /// <summary>Gets the manager's decision, Approved or Rejected; null while undecided.</summary>
    public string? ManagerDecision { get; init; }
}
