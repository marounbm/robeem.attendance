namespace robeem.Web.Actors;

/// <summary>
/// Who the screens are shown for. With a Development "act as" choice it is that choice; without
/// one the Api answers as its stand-in user, which holds every role, so every area is shown. This
/// only decides what to <i>show</i>: the Api is the authority and refuses what a role may not do.
/// </summary>
public interface ICurrentActor
{
    /// <summary>Gets a value indicating whether a choice was made.</summary>
    bool HasChoice { get; }

    ActingAs? Choice { get; }

    /// <summary>Gets a value indicating whether the actor holds the role (always true while no choice was made).</summary>
    bool IsInRole(string role);

    /// <summary>Gets a value indicating whether the actor holds any of the roles.</summary>
    bool IsInAnyRole(params string[] roles);
}
