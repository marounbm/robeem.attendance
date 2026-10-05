using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/integrations")]
public sealed class AttendanceIntegrationsController : ControllerBase
{
    private readonly IIntegrationHealthService _health;

    public AttendanceIntegrationsController(IIntegrationHealthService health)
    {
        _health = health;
    }

    /// <summary>The integration's stored health, its circuit breaker and the KPIs of the last day.</summary>
    [HttpGet("{id:int}/health")]
    public async Task<ActionResult<IntegrationHealthReport>> GetHealth(int id, CancellationToken cancellationToken) =>
        await _health.GetIntegrationHealthAsync(id, cancellationToken) is { } report ? report : NotFound();
}
