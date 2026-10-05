namespace robeem.Contracts.Attendance;

/// <summary>A reviewer's decision on a record waiting for review. Who decides is the authenticated user.</summary>
public sealed class DecideReviewRequest
{
    public bool Approved { get; init; }

    /// <summary>Gets why. Required when the record is rejected.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user decides as HR rather than as the employee's manager.
    /// Only read when the user holds both roles.
    /// </summary>
    public bool IsHrDecision { get; init; }
}
