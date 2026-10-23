namespace robeem.Contracts.Attendance;

/// <summary>A manager's or HR's acceptance of a break exception as it is. The decider is the authenticated user.</summary>
public sealed class AcceptExceptionRequest
{
    /// <summary>Gets why the exception is accepted. Required.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a user who is both manager and HR accepts as HR.</summary>
    public bool IsHrDecision { get; init; }
}
