using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using robeem.Api.Tenancy;
using robeem.Application.Tenancy;
using robeem.Contracts.Tenancy;
using robeem.Infrastructure.Tenancy;

namespace robeem.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public sealed class TenantsController : ControllerBase
{
    private readonly ITenantRegistry _registry;
    private readonly TenantContext _tenantContext;
    private readonly TenancyOptions _options;

    public TenantsController(ITenantRegistry registry, TenantContext tenantContext, IOptions<TenancyOptions> options)
    {
        _registry = registry;
        _tenantContext = tenantContext;
        _options = options.Value;
    }

    /// <summary>Tenants a caller may pick. Only available while unauthenticated tenant selection is allowed (development).</summary>
    [HttpGet]
    [AllowWithoutTenant]
    public ActionResult<IReadOnlyList<TenantSummary>> List()
    {
        if (!_options.AllowUnauthenticatedTenantSelection)
        {
            return NotFound();
        }

        return _registry.Tenants.Select(t => new TenantSummary { Id = t.Id, Name = t.Name }).ToList();
    }

    /// <summary>The tenant this request was resolved to (the middleware guarantees there is one).</summary>
    [HttpGet("current")]
    public CurrentTenantResponse Current()
    {
        var tenant = _tenantContext.Tenant ?? throw new TenantNotResolvedException();
        return new CurrentTenantResponse
        {
            Id = tenant.Id,
            Name = tenant.Name,
            CanSelectTenant = _options.AllowUnauthenticatedTenantSelection,
        };
    }
}
