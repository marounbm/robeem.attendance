using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The connections of an integration: the endpoint attendance comes from and how it is authenticated. A connection stores the
/// name of its secret, never the secret; a test answers whether it worked, never what the other end said.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/integrations/{integrationId:int}/connections")]
public sealed class AttendanceConnectionsController : ControllerBase
{
    private readonly IAttendanceConnectionService _connections;

    public AttendanceConnectionsController(IAttendanceConnectionService connections)
    {
        _connections = connections;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConnectionListItem>>> List(int integrationId, CancellationToken cancellationToken) =>
        await _connections.ListAsync(integrationId, cancellationToken) is { } list ? Ok(list) : NotFound();

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<ConnectionInput>> GetInput(int integrationId, int id, CancellationToken cancellationToken) =>
        await _connections.GetInputAsync(integrationId, id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(int integrationId, ConnectionInput input, CancellationToken cancellationToken)
    {
        var result = await _connections.CreateAsync(integrationId, input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int integrationId, int id, ConnectionInput input, CancellationToken cancellationToken)
    {
        var result = await _connections.UpdateAsync(integrationId, id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpPost("{id:int}/make-default")]
    public async Task<IActionResult> MakeDefault(int integrationId, int id, CancellationToken cancellationToken)
    {
        var result = await _connections.MakeDefaultAsync(integrationId, id, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>Tests the connection and records the outcome on it: 200 with whether it worked, in the server's own words.</summary>
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<ConnectionTestResult>> Test(int integrationId, int id, CancellationToken cancellationToken) =>
        await _connections.TestAsync(integrationId, id, cancellationToken) is { } result ? result : NotFound();

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
