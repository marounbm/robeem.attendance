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

    public static IReadOnlyList<NavSection> Sections { get; } = [People];

    /// <summary>The sections the actor may open, each with only the items it may open; empty sections are dropped.</summary>
    public static IReadOnlyList<NavSection> For(ICurrentActor actor) =>
        Sections
            .Select(group => group with { Items = group.Items.Where(item => item.Roles.Length == 0 || actor.IsInAnyRole(item.Roles)).ToList() })
            .Where(group => group.Items.Count > 0)
            .ToList();
}
