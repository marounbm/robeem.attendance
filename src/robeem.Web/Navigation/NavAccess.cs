namespace robeem.Web.Navigation;

/// <summary>Whether the acting user may open a screen, and if not, why.</summary>
public enum NavAccess
{
    Allowed,
    MissingRole,
    NeedsEmployee,
}
