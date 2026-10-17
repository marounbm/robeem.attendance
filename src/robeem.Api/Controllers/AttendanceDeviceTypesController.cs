using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>The kinds of punch device a tenant defines. A type is made inactive, never deleted (devices point at it).</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/device-types")]
public sealed class AttendanceDeviceTypesController : ControllerBase
{
    private readonly IAttendanceDeviceTypeService _types;

    public AttendanceDeviceTypesController(IAttendanceDeviceTypeService types)
    {
        _types = types;
    }

    [HttpGet]
    public async Task<IReadOnlyList<DeviceTypeItem>> List(CancellationToken cancellationToken) =>
        await _types.ListAsync(cancellationToken);

    /// <summary>The active types, to choose from on a device.</summary>
    [HttpGet("options")]
    public async Task<IReadOnlyList<DeviceTypeOption>> Options(CancellationToken cancellationToken) =>
        await _types.ListOptionsAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<DeviceTypeInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _types.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var result = await _types.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var result = await _types.UpdateAsync(id, input, cancellationToken);
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
