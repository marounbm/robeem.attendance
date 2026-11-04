namespace robeem.Api.Security;

/// <summary>The roles of study §16.5. A user can hold several; a manager is normally an employee too.</summary>
public static class AttendanceRoles
{
    public const string Employee = "Employee";

    public const string Manager = "Manager";

    public const string HRAdministrator = "HRAdministrator";

    public const string PayrollAdministrator = "PayrollAdministrator";

    public const string IntegrationAdministrator = "IntegrationAdministrator";

    /// <summary>
    /// Manages the attendance catalog (vendors, connectors, transports, device types, payload formats) in the prime tenant, for every
    /// tenant. Only effective when the request's tenant is the prime tenant: the catalog refuses a save from any other.
    /// </summary>
    public const string PlatformAdministrator = "PlatformAdministrator";

    /// <summary>The roles that read the catalog: the integration administrators who set integrations up from it, and the platform administrators who maintain it.</summary>
    public const string CatalogReaders = IntegrationAdministrator + "," + PlatformAdministrator;

    /// <summary>The roles that decide corrections, for <c>[Authorize(Roles = ...)]</c>.</summary>
    public const string Approvers = Manager + "," + HRAdministrator;

    /// <summary>The roles that look at exceptions: their own, their reports', or everyone's.</summary>
    public const string ExceptionViewers = Employee + "," + Manager + "," + HRAdministrator;

    /// <summary>The roles that look at the background jobs and their history: the integration administrators who run them, and HR whose screens depend on them.</summary>
    public const string JobViewers = IntegrationAdministrator + "," + HRAdministrator;

    /// <summary>The roles that look at the raw and normalized punches: the integration administrators who troubleshoot ingestion, and HR who investigate a punch.</summary>
    public const string TransactionViewers = IntegrationAdministrator + "," + HRAdministrator;

    public static readonly IReadOnlyList<string> All = [Employee, Manager, HRAdministrator, PayrollAdministrator, IntegrationAdministrator, PlatformAdministrator];
}
