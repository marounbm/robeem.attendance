namespace robeem.Contracts.Security;

/// <summary>Who the Api answers as when a request names nobody in particular (its Development stand-in user).</summary>
public sealed class StandInActor
{
    /// <summary>Gets the <c>Profile</c> id of the stand-in user, or null when it is linked to no employee.</summary>
    public int? EmployeeId { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}
