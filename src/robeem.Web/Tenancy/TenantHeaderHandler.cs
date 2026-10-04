using robeem.Contracts.Tenancy;

namespace robeem.Web.Tenancy;

/// <summary>
/// Forwards the tenant picked in the front end (<see cref="TenantCookie"/>) to robeem.Api as the
/// <see cref="TenancyProtocol.TenantHeader"/> header. The API honours it only while unauthenticated
/// tenant selection is allowed (development); once authentication exists, the tenant comes from
/// the user's token instead.
/// </summary>
public sealed class TenantHeaderHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantHeaderHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext?.Request is { } incoming && TenantCookie.Read(incoming) is { } tenantId)
        {
            request.Headers.Remove(TenancyProtocol.TenantHeader);
            request.Headers.Add(TenancyProtocol.TenantHeader, tenantId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
