using robeem.Web.ApiClients;

namespace robeem.Web.Actors;

public sealed class CurrentActor : ICurrentActor
{
    private readonly Lazy<ActingAs?> _choice;
    private readonly IStandInApiClient _standIn;
    private readonly StandInCache _cache;

    public CurrentActor(IHttpContextAccessor httpContextAccessor, IStandInApiClient standIn, StandInCache cache)
    {
        _standIn = standIn;
        _cache = cache;
        _choice = new Lazy<ActingAs?>(() => httpContextAccessor.HttpContext?.Request is { } request ? ActorCookie.Read(request) : null);
    }

    public bool HasChoice => _choice.Value is not null;

    public ActingAs? Choice => _choice.Value;

    public ActingAs? Effective => _choice.Value ?? _cache.Value;

    public async Task ResolveAsync(CancellationToken cancellationToken)
    {
        if (_choice.Value is not null || _cache.Known)
        {
            return;
        }

        try
        {
            var standIn = await _standIn.GetAsync(cancellationToken);
            _cache.Set(standIn is null ? null : new ActingAs(standIn.EmployeeId, "Development user", standIn.Roles));
        }
        catch (Exception ex) when (ex is HttpRequestException or TenantSelectionRequiredException)
        {
            // The Api is unreachable, refused the question or rejected the tenant: stay unknown (every role
            // assumed) and ask again next time. The screen's own call reports a real problem.
        }
    }

    public bool IsInRole(string role) => Effective is not { } actor || actor.Roles.Contains(role, StringComparer.Ordinal);

    public bool IsInAnyRole(params string[] roles) => roles.Any(IsInRole);
}
