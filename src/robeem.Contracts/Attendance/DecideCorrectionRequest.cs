namespace robeem.Contracts.Attendance;

/// <summary>
/// An approver's decision on a pending correction. Who decides comes from the authenticated user,
/// not from the body.
/// </summary>
public sealed class DecideCorrectionRequest
{
    public bool Approved { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user decides as HR rather than as manager. Only read
    /// when the user holds both roles; a user with one role decides in that role.
    /// </summary>
    public bool IsHrDecision { get; init; }
}
