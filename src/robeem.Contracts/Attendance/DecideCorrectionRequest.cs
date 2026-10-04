namespace robeem.Contracts.Attendance;

/// <summary>An approver's decision on a pending correction.</summary>
public sealed class DecideCorrectionRequest
{
    /// <summary>
    /// Gets the employee (<c>Profile</c>) who decides.
    /// TODO(P12): take this from the authenticated user and check the user's role; there is no
    /// authentication or role system in this solution yet (CLAUDE.md §2.8).
    /// </summary>
    public int DecidedByUserId { get; init; }

    public bool Approved { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the HR decision rather than the manager's.
    /// TODO(P12): derive it from the user's role instead of trusting the caller.
    /// </summary>
    public bool IsHrDecision { get; init; }
}
