namespace robeem.Web.Actors;

/// <summary>
/// The Api's stand-in user, asked for once and kept: it comes from the Api's configuration, which
/// does not change while it runs. Restart the Web after changing it. Singleton.
/// </summary>
public sealed class StandInCache
{
    private ActingAs? _value;

    /// <summary>Gets a value indicating whether the Api has answered (with a user, or with none).</summary>
    public bool Known { get; private set; }

    /// <summary>Gets the stand-in user, or null when the Api has none or has not been asked.</summary>
    public ActingAs? Value => _value;

    public void Set(ActingAs? value)
    {
        _value = value;
        Known = true;
    }
}
