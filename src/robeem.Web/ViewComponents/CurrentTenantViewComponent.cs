using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Tenancy;
using robeem.Web.ApiClients;

namespace robeem.Web.ViewComponents;

/// <summary>The navbar's "Tenant: ... Switch" label.</summary>
public sealed class CurrentTenantViewComponent : ViewComponent
{
    private readonly ITenantsApiClient _tenants;
    private readonly ILogger<CurrentTenantViewComponent> _logger;

    public CurrentTenantViewComponent(ITenantsApiClient tenants, ILogger<CurrentTenantViewComponent> logger)
    {
        _tenants = tenants;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        CurrentTenantResponse? tenant = null;
        try
        {
            tenant = await _tenants.GetCurrentAsync(HttpContext.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            // The layout also renders the error page, which must not fail when the API is down.
            _logger.LogWarning(ex, "Could not read the current tenant from robeem.Api.");
        }

        return View(tenant);
    }
}
