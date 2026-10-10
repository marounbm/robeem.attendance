using robeem.Web.Actors;

namespace robeem.Web.Navigation;

/// <summary>
/// The one list the navbar and the start page both read. A screen is shown when the acting roles
/// include one of its roles; the Api stays the authority and refuses the rest. A new screen is one
/// <see cref="NavItem"/> here.
/// </summary>
public static class NavigationMenu
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
    public const string Hr = "HRAdministrator";
    public const string Payroll = "PayrollAdministrator";
    public const string Integration = "IntegrationAdministrator";

    private static readonly NavSection People = new(
        "People",
        [
            new NavItem("Employees", "Employees", "Index", "Add and edit employees, their time schedule and their manager.", Hr),
            new NavItem("Time schedules", "TimeSchedules", "Index", "Define the shifts, the weekly pattern and the teams that every day is calculated against.", Hr),
        ]);

    private static readonly NavSection MyWork = new(
        "My work",
        [
            new NavItem("Attendance", "Records", "Index", "Calculated days: hours worked, overtime, status and how each was worked out.", Employee, Manager, Hr) { NeedsEmployee = true },
            new NavItem("Exceptions", "Exceptions", "Index", "Missing punches and other problems to settle: propose the missing time.", Employee, Manager, Hr) { NeedsEmployee = true },
        ]);

    private static readonly NavSection Approvals = new(
        "Approvals",
        [
            new NavItem("Corrections", "Corrections", "Index", "Decide the corrections employees proposed for missing punches.", Manager, Hr) { NeedsEmployee = true },
            new NavItem("Reviews", "Reviews", "Index", "Approve or reject overtime, unscheduled work and incomplete days.", Manager, Hr) { NeedsEmployee = true },
        ]);

    private static readonly NavSection Integrations = new("Integrations", [new NavItem("Integrations", "Integrations", "Index", "Connections to time-clock providers: devices, employee-id mappings, health, uploads and sync.", Integration)]);

    private static readonly NavSection PayrollSection = new(
        "Payroll",
        [
            new NavItem("Payroll", "Payroll", "Index", "The approved days payroll may pay for, per period, with a CSV export.", Payroll),
            new NavItem("Pay periods", "PayPeriods", "Index", "Create, close and reopen the pay periods; closing ends payroll extraction of a period.", Payroll),
        ]);

    private static readonly NavSection Settings = new("Settings", [new NavItem("Policies", "Policies", "Index", "Rounding, grace periods, overtime and correction rules: the tenant default and each schedule's own.", Hr)]);

    private static readonly NavSection AuditSection = new("Audit", [new NavItem("Audit log", "Audit", "Index", "Who decided, changed or read what, newest first.", Hr)]);

    public static IReadOnlyList<NavSection> Sections { get; } = [People, MyWork, Approvals, Integrations, PayrollSection, Settings, AuditSection];

    /// <summary>
    /// The sections the actor may open, each with only the items it may open; empty sections are dropped.
    /// The menu and the start page both read this, so a screen the actor cannot open is shown nowhere.
    /// </summary>
    public static IReadOnlyList<NavSection> For(ICurrentActor actor) =>
        Sections
            .Select(group => group with { Items = group.Items.Where(item => AccessTo(actor, item) == NavAccess.Allowed).ToList() })
            .Where(group => group.Items.Count > 0)
            .ToList();

    /// <summary>Gets the screen of a controller, or null for one that is not in the menu (the tenant picker, the start page).</summary>
    public static NavItem? Find(string? controller) =>
        Sections.SelectMany(section => section.Items).FirstOrDefault(i => string.Equals(i.Controller, controller, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the actor may open the screen. The same rule decides what the menu shows and what
    /// <see cref="ActorAccessFilter"/> lets through, so the two cannot disagree. While who the actor is
    /// stays unknown (the Api could not be asked, or production), everything is allowed and the Api decides.
    /// </summary>
    public static NavAccess AccessTo(ICurrentActor actor, NavItem item)
    {
        if (item.Roles.Length > 0 && !actor.IsInAnyRole(item.Roles))
        {
            return NavAccess.MissingRole;
        }

        return item.NeedsEmployee && actor.Effective is { EmployeeId: null } && !actor.IsInRole(Hr)
            ? NavAccess.NeedsEmployee
            : NavAccess.Allowed;
    }
}
