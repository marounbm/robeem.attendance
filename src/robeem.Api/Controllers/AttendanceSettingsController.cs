using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>The attendance settings of the tenant: the date the punching started. Every change is audited.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/attendance/settings")]
public sealed class AttendanceSettingsController : ControllerBase
{
    private readonly IAttendanceSettingsService _settings;

    public AttendanceSettingsController(IAttendanceSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<AttendanceSettingsItem> Get(CancellationToken cancellationToken) =>
        await _settings.GetAsync(cancellationToken);

    [HttpPut]
    public async Task<IActionResult> Save(AttendanceSettingsInput input, CancellationToken cancellationToken)
    {
        var result = await _settings.SaveAsync(input, cancellationToken);
        if (result.Succeeded)
        {
            return NoContent();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
