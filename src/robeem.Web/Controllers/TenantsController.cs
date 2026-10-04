using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Tenancy;

namespace robeem.Web.Controllers;

/// <summary>Tenant picker. Only available while robeem.Api allows unauthenticated tenant selection (development).</summary>
public sealed class TenantsController : Controller
{
    private readonly ITenantsApiClient _tenants;

    public TenantsController(ITenantsApiClient tenants)
    {
        _tenants = tenants;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl, CancellationToken cancellationToken)
    {
        if (await _tenants.ListAsync(cancellationToken) is not { } tenants)
        {
            return NotFound();
        }

        return View(new TenantPickerViewModel
        {
            Tenants = tenants,
            CurrentTenantId = (await _tenants.GetCurrentAsync(cancellationToken))?.Id,
            ReturnUrl = returnUrl,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Select(string tenantId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (await _tenants.ListAsync(cancellationToken) is not { } tenants)
        {
            return NotFound();
        }

        if (tenants.FirstOrDefault(t => string.Equals(t.Id, tenantId, StringComparison.OrdinalIgnoreCase)) is not { } tenant)
        {
            return BadRequest("Unknown tenant.");
        }

        Response.Cookies.Append(TenantCookie.Name, tenant.Id, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
        });

        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
