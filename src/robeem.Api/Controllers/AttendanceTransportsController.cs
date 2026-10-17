using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The tenant's names for the six ways attendance reaches robeem. Their behaviour is code, so a transport can be
/// relabeled or switched off for new integrations, but not added.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/transports")]
public sealed class AttendanceTransportsController : ControllerBase
{
    private readonly IAttendanceTransportService _transports;

    public AttendanceTransportsController(IAttendanceTransportService transports)
    {
        _transports = transports;
    }

    [HttpGet]
    public async Task<IReadOnlyList<TransportItem>> List(CancellationToken cancellationToken) =>
        await _transports.ListAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<TransportInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _transports.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TransportInput input, CancellationToken cancellationToken)
    {
        var result = await _transports.UpdateAsync(id, input, cancellationToken);
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
