using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using robeem.Infrastructure.Tenancy;

namespace robeem.Web.Tenancy;

/// <summary>
/// Resolves the request's tenant into the scoped <see cref="TenantContext"/>, which selects the
/// tenant's database. Sources, in order:
/// <list type="number">
/// <item>the authenticated user's <c>tenant_id</c> claim (the only source in production);</item>
/// <item>when <see cref="TenancyOptions.AllowUnauthenticatedTenantSelection"/> is on (development):
/// the <c>X-Tenant-Id</c> header, then the tenant cookie set by the tenant picker, then
/// <see cref="TenancyOptions.DefaultTenantId"/>.</item>
/// </list>
/// A controller action that needs a tenant never runs without one.
/// </summary>
public sealed class TenantResolutionMiddleware : IMiddleware
{
    public const string TenantClaim = "tenant_id";
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantCookie = "robeem.tenant";

    private readonly TenantContext _tenantContext;
    private readonly ITenantRegistry _registry;
    private readonly TenancyOptions _options;

    public TenantResolutionMiddleware(TenantContext tenantContext, ITenantRegistry registry, IOptions<TenancyOptions> options)
    {
        _tenantContext = tenantContext;
        _registry = registry;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var claimTenant = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(TenantClaim)?.Value : null;

        if (claimTenant is not null)
        {
            if (_registry.Find(claimTenant) is not { } tenant)
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Your account's tenant is not available.");
                return;
            }

            _tenantContext.SetTenant(tenant.Id);
        }
        else if (_options.AllowUnauthenticatedTenantSelection)
        {
            var header = context.Request.Headers[TenantHeader].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                if (_registry.Find(header) is not { } tenant)
                {
                    await RejectAsync(context, StatusCodes.Status400BadRequest, "Unknown tenant.");
                    return;
                }

                _tenantContext.SetTenant(tenant.Id);
            }
            else if (context.Request.Cookies.TryGetValue(TenantCookie, out var cookie) && _registry.Find(cookie) is { } cookieTenant)
            {
                _tenantContext.SetTenant(cookieTenant.Id);
            }
            else if (_registry.DefaultTenant is { } defaultTenant)
            {
                _tenantContext.SetTenant(defaultTenant.Id);
            }
        }

        if (_tenantContext.Tenant is null && RequiresTenant(context))
        {
            if (_options.AllowUnauthenticatedTenantSelection && HttpMethods.IsGet(context.Request.Method))
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                context.Response.Redirect($"{context.Request.PathBase}/Tenants?returnUrl={Uri.EscapeDataString(returnUrl)}");
                return;
            }

            await RejectAsync(context, StatusCodes.Status403Forbidden, "No tenant could be resolved for this request.");
            return;
        }

        await next(context);
    }

    private static bool RequiresTenant(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null
            && endpoint.Metadata.GetMetadata<AllowWithoutTenantAttribute>() is null;
    }

    private static Task RejectAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsync(message);
    }
}
