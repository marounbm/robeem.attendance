using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Api.Controllers;

/// <summary>
/// Attendance policies: the tenant-wide default and the optional policy of each time schedule.
/// A change applies to days processed from then on, and is audited.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/attendance/policies")]
public sealed class AttendancePoliciesController : ControllerBase
{
    private readonly IAttendancePolicyService _policies;

    public AttendancePoliciesController(IAttendancePolicyService policies)
    {
        _policies = policies;
    }

    [HttpGet]
    public async Task<IReadOnlyList<PolicyListItem>> List(CancellationToken cancellationToken) =>
        await _policies.ListAsync(cancellationToken);

    [HttpGet("schedule-options")]
    public async Task<IReadOnlyList<ScheduleOption>> ScheduleOptions(CancellationToken cancellationToken) =>
        await _policies.ListScheduleOptionsAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<PolicyInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _policies.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(PolicyInput input, CancellationToken cancellationToken)
    {
        var result = await _policies.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PolicyInput input, CancellationToken cancellationToken)
    {
        var result = await _policies.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _policies.DeleteAsync(id, cancellationToken);
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
