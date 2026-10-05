using robeem.Contracts.Security;

namespace robeem.Web.Actors;

/// <summary>
/// Forwards the Development "act as" choice (<see cref="ActorCookie"/>) to robeem.Api as the
/// <see cref="ActorProtocol.Header"/> header. The Api honours it only through its development
/// stand-in user; once real sign-in exists, the user's token replaces it.
/// </summary>
public sealed class ActorHeaderHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ActorHeaderHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext?.Request is { } incoming && ActorCookie.ReadHeaderValue(incoming) is { } value)
        {
            request.Headers.Remove(ActorProtocol.Header);
            request.Headers.Add(ActorProtocol.Header, value);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
