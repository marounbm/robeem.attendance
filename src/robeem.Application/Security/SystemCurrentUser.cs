namespace robeem.Application.Security;

/// <summary>The actor of work no user started: the scheduled jobs. It is in no role.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public const string SystemActor = "system";

    public bool IsAuthenticated => false;

    public int? EmployeeId => null;

    public string Actor => SystemActor;

    public bool IsInRole(string role) => false;
}
