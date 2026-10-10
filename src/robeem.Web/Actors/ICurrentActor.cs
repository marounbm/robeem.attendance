namespace robeem.Web.Actors;

/// <summary>
/// Who the screens are shown for. With a Development "act as" choice it is that choice; without
/// one the Api answers as its stand-in user, which <see cref="ResolveAsync"/> asks the Api for. Until
/// that is known (or in production, where there is no stand-in) every role is assumed. This only
/// decides what to <i>show</i>: the Api is the authority and refuses what a role may not do.
/// </summary>
public interface ICurrentActor
{
    /// <summary>Gets a value indicating whether a choice was made.</summary>
    bool HasChoice { get; }

    /// <summary>Gets the "act as" choice, or null when none was made.</summary>
    ActingAs? Choice { get; }

    /// <summary>Gets who the Api will treat the request as: the choice, else its stand-in user once known, else null (unknown).</summary>
    ActingAs? Effective => Choice;

    /// <summary>Asks the Api for its stand-in user when no choice was made and it is not known yet. Never throws for an unreachable Api.</summary>
    Task ResolveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Gets a value indicating whether the actor holds the role (always true while who they are is unknown).</summary>
    bool IsInRole(string role);

    /// <summary>Gets a value indicating whether the actor holds any of the roles.</summary>
    bool IsInAnyRole(params string[] roles);
}
