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

    /// <summary>The roles that look at exceptions: their own, their reports', or everyone's.</summary>
    public const string ExceptionViewers = Employee + "," + Manager + "," + HRAdministrator;

    /// <summary>The roles that look at the background jobs and their history: the integration administrators who run them, and HR whose screens depend on them.</summary>
    public const string JobViewers = IntegrationAdministrator + "," + HRAdministrator;

    /// <summary>The roles that look at the raw and normalized punches: the integration administrators who troubleshoot ingestion, and HR who investigate a punch.</summary>
    public const string TransactionViewers = IntegrationAdministrator + "," + HRAdministrator;

    public static readonly IReadOnlyList<string> All = [Employee, Manager, HRAdministrator, PayrollAdministrator, IntegrationAdministrator];
}
