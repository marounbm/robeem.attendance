namespace robeem.Contracts.Attendance;

/// <summary>A decision on one line (a date and a kind) of the compensation report.</summary>
public sealed class CompensationLineDecision
{
    public DateTime Date { get; init; }

    /// <summary>Gets the kind of line: Overtime or Undertime.</summary>
    public string LineKind { get; init; } = string.Empty;

    public string Decision { get; init; } = string.Empty;

    /// <summary>Gets the type of leave a paid-leave decision adds; Annual when not given.</summary>
    public string? LeaveType { get; init; }

    public string? Note { get; init; }
}
