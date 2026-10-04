using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using robeem.Application.Tenancy;
using robeem.Contracts.Tenancy;
using robeem.Infrastructure.Tenancy;

namespace robeem.Api.Tenancy;

/// <summary>
/// Resolves the request's tenant into the scoped <see cref="TenantContext"/>, which selects the
/// tenant's database. Sources, in order:
/// <list type="number">
/// <item>the authenticated user's <c>tenant_id</c> claim (the only source in production);</item>
/// <item>when <see cref="TenancyOptions.AllowUnauthenticatedTenantSelection"/> is on (development):
/// the <see cref="TenancyProtocol.TenantHeader"/> header, then <see cref="TenancyOptions.DefaultTenantId"/>.</item>
/// </list>
/// A controller action that needs a tenant never runs without one. Rejections are ProblemDetails
/// carrying a <see cref="TenancyProtocol"/> error code, so the front end can react to them.
/// </summary>
public sealed class TenantResolutionMiddleware : IMiddleware
{
    public const string TenantClaim = "tenant_id";

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
                await RejectAsync(context, StatusCodes.Status403Forbidden, TenancyProtocol.TenantUnavailableCode, "Your account's tenant is not available.");
                return;
            }

            _tenantContext.SetTenant(tenant.Id);
        }
        else if (_options.AllowUnauthenticatedTenantSelection)
        {
            var header = context.Request.Headers[TenancyProtocol.TenantHeader].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                if (_registry.Find(header) is not { } tenant)
                {
                    await RejectAsync(context, StatusCodes.Status400BadRequest, TenancyProtocol.UnknownTenantCode, "Unknown tenant.");
                    return;
                }

                _tenantContext.SetTenant(tenant.Id);
            }
            else if (_registry.DefaultTenant is { } defaultTenant)
            {
                _tenantContext.SetTenant(defaultTenant.Id);
            }
        }

        if (_tenantContext.Tenant is null && RequiresTenant(context))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, TenancyProtocol.TenantRequiredCode, "No tenant could be resolved for this request.");
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

    private static Task RejectAsync(HttpContext context, int statusCode, string code, string message)
        => Results.Problem(
                statusCode: statusCode,
                title: message,
                extensions: new Dictionary<string, object?> { [TenancyProtocol.ErrorCodeExtension] = code })
            .ExecuteAsync(context);
}
