namespace robeem.Application.Security;

/// <summary>
/// Who is acting. In the Api it is the authenticated user (from the token's claims); everywhere
/// else (background jobs, tests) it is <see cref="SystemCurrentUser"/>.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Gets the user's <c>Profile</c> id, when the token carries one.</summary>
    int? EmployeeId { get; }

    /// <summary>Gets the actor as written in the audit log: <c>employee:{id}</c>, or <c>system</c>.</summary>
    string Actor { get; }

    bool IsInRole(string role);
}
