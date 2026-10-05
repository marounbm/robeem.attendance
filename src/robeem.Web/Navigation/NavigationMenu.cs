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

    private static readonly NavSection People = new("People", [new NavItem("Employees", "Employees", "Index", "Add and edit employees, their time schedule and their manager.", Hr)]);

    private static readonly NavSection MyWork = new("My work", [new NavItem("Exceptions", "Exceptions", "Index", "Missing punches and other problems to settle: propose the missing time.", Employee, Manager, Hr)]);

    private static readonly NavSection Approvals = new(
        "Approvals",
        [
            new NavItem("Corrections", "Corrections", "Index", "Decide the corrections employees proposed for missing punches.", Manager, Hr),
            new NavItem("Reviews", "Reviews", "Index", "Approve or reject overtime, unscheduled work and incomplete days.", Manager, Hr),
        ]);

    private static readonly NavSection Integrations = new("Integrations", [new NavItem("Integrations", "Integrations", "Index", "Connections to time-clock providers: devices, employee-id mappings, health, uploads and sync.", Integration)]);

    public static IReadOnlyList<NavSection> Sections { get; } = [People, MyWork, Approvals, Integrations];

    /// <summary>The sections the actor may open, each with only the items it may open; empty sections are dropped.</summary>
    public static IReadOnlyList<NavSection> For(ICurrentActor actor) =>
        Sections
            .Select(group => group with { Items = group.Items.Where(item => item.Roles.Length == 0 || actor.IsInAnyRole(item.Roles)).ToList() })
            .Where(group => group.Items.Count > 0)
            .ToList();
}
