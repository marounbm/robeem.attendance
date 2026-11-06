namespace robeem.Contracts.Attendance;

public sealed class DecideApprovalRequest
{
    public bool Approved { get; init; }

    /// <summary>Gets why. Required when a review is rejected.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user decides as HR rather than as the employee's manager. Only read when the user holds
    /// both roles; a user with one role decides in that role.
    /// </summary>
    public bool IsHrDecision { get; init; }
}
