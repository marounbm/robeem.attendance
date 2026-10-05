namespace robeem.Api.Security;

/// <summary>The roles of study §16.5. A user can hold several; a manager is normally an employee too.</summary>
public static class AttendanceRoles
{
    public const string Employee = "Employee";

    public const string Manager = "Manager";

    public const string HRAdministrator = "HRAdministrator";

    public const string PayrollAdministrator = "PayrollAdministrator";

    public const string IntegrationAdministrator = "IntegrationAdministrator";

    /// <summary>The roles that decide corrections, for <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Approvers = Manager + "," + HRAdministrator;

    public static readonly IReadOnlyList<string> All = [Employee, Manager, HRAdministrator, PayrollAdministrator, IntegrationAdministrator];
}
