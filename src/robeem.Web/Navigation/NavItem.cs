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
}
