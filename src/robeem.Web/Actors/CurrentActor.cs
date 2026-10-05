namespace robeem.Web.Actors;

public sealed class CurrentActor : ICurrentActor
{
    private readonly Lazy<ActingAs?> _choice;

    public CurrentActor(IHttpContextAccessor httpContextAccessor)
    {
        _choice = new Lazy<ActingAs?>(() => httpContextAccessor.HttpContext?.Request is { } request ? ActorCookie.Read(request) : null);
    }

    public bool HasChoice => _choice.Value is not null;

    public ActingAs? Choice => _choice.Value;

    public bool IsInRole(string role) => _choice.Value is not { } choice || choice.Roles.Contains(role, StringComparer.Ordinal);

    public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
}
