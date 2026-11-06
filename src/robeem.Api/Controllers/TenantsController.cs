using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Api.Maintenance;
using robeem.Api.Tenancy;
using robeem.Application.Tenancy;
using robeem.Contracts.Tenancy;
using robeem.Infrastructure.Persistence;
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
            IsPrime = string.Equals(tenant.Id, _registry.PrimeTenant.Id, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// 204 when the request's tenant database answers; 503 with the <c>database_unavailable</c> code when it does not (the
    /// maintenance middleware answers the same while the Api has not finished preparing the databases). It reads no data.
    /// </summary>
    [HttpGet("current/database")]
    public async Task<IActionResult> CheckDatabase([FromServices] RobeemDbContext database, CancellationToken cancellationToken)
    {
        if (await database.Database.CanConnectAsync(cancellationToken))
        {
            return NoContent();
        }

        return new ObjectResult(MaintenanceModeMiddleware.CreateProblem("The database is not reachable. The application is in maintenance mode."))
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
    }
}
