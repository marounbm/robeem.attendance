using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using robeem.Infrastructure.Tenancy;
using robeem.Web.Models;
using robeem.Web.Tenancy;

namespace robeem.Web.Controllers;

/// <summary>Tenant picker. Only available while unauthenticated tenant selection is allowed (development).</summary>
[AllowWithoutTenant]
public sealed class TenantsController : Controller
{
    private readonly ITenantRegistry _registry;
    private readonly ITenantContext _tenantContext;
    private readonly TenancyOptions _options;

    public TenantsController(ITenantRegistry registry, ITenantContext tenantContext, IOptions<TenancyOptions> options)
    {
        _registry = registry;
        _tenantContext = tenantContext;
        _options = options.Value;
    }

    [HttpGet]
    public IActionResult Index(string? returnUrl)
    {
        if (!_options.AllowUnauthenticatedTenantSelection)
        {
            return NotFound();
        }

        return View(new TenantPickerViewModel
        {
            Tenants = _registry.Tenants,
            CurrentTenantId = _tenantContext.TenantId,
            ReturnUrl = returnUrl,
        });
    }

    [HttpPost]
    public IActionResult Select(string tenantId, string? returnUrl)
    {
        if (!_options.AllowUnauthenticatedTenantSelection)
        {
            return NotFound();
        }

        if (_registry.Find(tenantId) is not { } tenant)
        {
            return BadRequest("Unknown tenant.");
        }

        Response.Cookies.Append(TenantResolutionMiddleware.TenantCookie, tenant.Id, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
        });

        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
