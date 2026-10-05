namespace robeem.Web.Navigation;

/// <summary>A group of screens under one menu heading.</summary>
public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);
