namespace robeem.Web.Navigation;

/// <summary>One screen in the menu. <see cref="Roles"/> empty means every role may open it.</summary>
public sealed record NavItem(string Title, string Controller, string Action, string Description, params string[] Roles);
