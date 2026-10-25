using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The catalog of connectors: the software and protocols each vendor offers, with their transports, default payload format
/// and code adapter. A connector is made inactive, never deleted (integrations point at it). To choose a connector for an
/// integration, see <c>api/attendance/integrations/connectors</c>.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/connectors")]
public sealed class AttendanceConnectorsController : ControllerBase
{
    private readonly IAttendanceConnectorService _connectors;

    public AttendanceConnectorsController(IAttendanceConnectorService connectors)
    {
        _connectors = connectors;
    }

    [HttpGet]
    public async Task<IReadOnlyList<ConnectorListItem>> List(CancellationToken cancellationToken) =>
        await _connectors.ListAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<ConnectorInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _connectors.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(ConnectorInput input, CancellationToken cancellationToken)
    {
        var result = await _connectors.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ConnectorInput input, CancellationToken cancellationToken)
    {
        var result = await _connectors.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    private ActionResult Failure(SaveResult result)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
