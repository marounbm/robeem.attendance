namespace robeem.Web.Navigation;

/// <summary>One screen in the menu. <see cref="Roles"/> empty means every role may open it.</summary>
public sealed record NavItem(string Title, string Controller, string Action, string Description, params string[] Roles)
{
    /// <summary>
    /// Gets a value indicating whether the screen shows what an employee's record may see, so an actor
    /// linked to no employee cannot open it (HR, who see everyone, excepted). Mirrors the controller's
    /// <see cref="Actors.RequiresEmployeeAttribute"/> with <c>HrExempt</c>; a test keeps the two in step.
    /// </summary>
    public bool NeedsEmployee { get; init; }

    /// <summary>
    /// Gets a value indicating whether the screen manages platform data, which only the prime tenant owns (the attendance catalog):
    /// it is not offered in any other tenant, and the Api refuses what it does there.
    /// </summary>
    public bool PrimeTenantOnly { get; init; }

    /// <summary>
    /// Gets the controller of the menu screen this one is a tab of, or null for a screen of its own. A tab is not offered in the
    /// menu or on the start page (its screen is), but its access is checked like any other screen's.
    /// </summary>
    public string? TabOf { get; init; }
}
