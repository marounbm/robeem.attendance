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

    // Running the integrations, day to day: what is connected, the machines, getting data in, and the jobs that process it.
    private static readonly NavSection Integrations = new(
        "Integrations",
        [
            new NavItem("Integrations", "Integrations", "Index", "Your use of each vendor's software: its connections, health, schedule and sync.", Integration),
            new NavItem("Devices", "Devices", "Index", "The punch devices of every integration, their time zones, and each device's employee-id mappings.", Integration),
            new NavItem("Upload attendance", "Uploads", "Index", "Upload a file exported from the time clock, in the format and layout of its integration.", Hr),
            new NavItem("Background jobs", "Jobs", "Index", "When the jobs that normalize punches, calculate days and sync integrations run next, how each run ended, and run one now.", Integration, Hr),
        ]);

    // What came in, before it becomes a day: every punch as received, then as normalized.
    private static readonly NavSection Transactions = new(
        "Transactions",
        [
            new NavItem("Raw transactions", "RawTransactions", "Index", "Every punch exactly as the time clock or file sent it, with its status and the normalized punch it became.", Integration, Hr),
            new NavItem("Normalized transactions", "NormalizedTransactions", "Index", "Every punch after normalization: employee, device, event type, certainty, duplicates and flags.", Integration, Hr),
        ]);

    // What an integration is built from, in the order it is built: the company, its software, how its files are read, how data arrives, the kinds of device.
    private static readonly NavSection IntegrationSetup = new(
        "Integration setup",
        [
            new NavItem("Vendors", "Vendors", "Index", "The companies you receive attendance from.", Integration),
            new NavItem("Connectors", "Connectors", "Index", "The software and protocols of each vendor: their transports, the payload format their uploads are read by, and the code adapter, if any.", Integration),
            new NavItem("Payload formats", "PayloadFormats", "Index", "Define the layout of a file an integration reads on manual upload: columns, date patterns and type labels.", Integration),
            new NavItem("Transports", "Transports", "Index", "Rename the ways attendance reaches robeem, or switch one off for new integrations.", Integration),
            new NavItem("Device types", "DeviceTypes", "Index", "The kinds of punch device a device can be given.", Integration),
        ]);

    private static readonly NavSection PayrollSection = new(
        "Payroll",
        [
            new NavItem("Payroll", "Payroll", "Index", "The approved days payroll may pay for, per period, with a CSV export.", Payroll),
            new NavItem("Pay periods", "PayPeriods", "Index", "Create, close and reopen the pay periods; closing ends payroll extraction of a period.", Payroll),
        ]);

    private static readonly NavSection Settings = new("Settings", [new NavItem("Policies", "Policies", "Index", "Rounding, grace periods, overtime and correction rules: the tenant default and each schedule's own.", Hr)]);

    private static readonly NavSection AuditSection = new("Audit", [new NavItem("Audit log", "Audit", "Index", "Who decided, changed or read what, newest first.", Hr)]);

    public static IReadOnlyList<NavSection> Sections { get; } = [People, MyWork, Approvals, Integrations, Transactions, IntegrationSetup, PayrollSection, Settings, AuditSection];

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
