using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Common;
using robeem.Application.HR.Schedules;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Schedules;

namespace robeem.Api.Controllers;

/// <summary>
/// Defining time schedules: the pattern, its shifts, teams and rosters. Attendance is calculated
/// against these, so only HR may change them, and every change is audited. A change applies to days
/// processed from then on.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/time-schedules")]
public sealed class TimeSchedulesController : ControllerBase
{
    private readonly ITimeScheduleService _schedules;

    public TimeSchedulesController(ITimeScheduleService schedules)
    {
        _schedules = schedules;
    }

    [HttpGet]
    public async Task<IReadOnlyList<TimeScheduleListItem>> List(CancellationToken cancellationToken) =>
        await _schedules.ListAsync(cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TimeScheduleDetail>> Get(int id, CancellationToken cancellationToken) =>
        await _schedules.GetAsync(id, cancellationToken) is { } detail ? detail : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(TimeScheduleInput input, CancellationToken cancellationToken) =>
        Created(await _schedules.CreateAsync(input, cancellationToken));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TimeScheduleInput input, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.UpdateAsync(id, input, cancellationToken));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.DeleteAsync(id, cancellationToken));

    [HttpPost("{id:int}/shifts")]
    public async Task<ActionResult<CreatedResponse>> AddShift(int id, ScheduleShiftInput input, CancellationToken cancellationToken) =>
        Created(await _schedules.AddShiftAsync(id, input, cancellationToken));

    [HttpPut("{id:int}/shifts/{shiftId:int}")]
    public async Task<IActionResult> UpdateShift(int id, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.UpdateShiftAsync(id, shiftId, input, cancellationToken));

    [HttpDelete("{id:int}/shifts/{shiftId:int}")]
    public async Task<IActionResult> DeleteShift(int id, int shiftId, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.DeleteShiftAsync(id, shiftId, cancellationToken));

    [HttpPost("{id:int}/teams")]
    public async Task<ActionResult<CreatedResponse>> AddTeam(int id, ScheduleTeamInput input, CancellationToken cancellationToken) =>
        Created(await _schedules.AddTeamAsync(id, input, cancellationToken));

    [HttpPut("{id:int}/teams/{teamId:int}")]
    public async Task<IActionResult> RenameTeam(int id, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.RenameTeamAsync(id, teamId, input, cancellationToken));

    [HttpDelete("{id:int}/teams/{teamId:int}")]
    public async Task<IActionResult> DeleteTeam(int id, int teamId, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.DeleteTeamAsync(id, teamId, cancellationToken));

    /// <summary>Replaces the team's whole roster: the shifts worked on each day of the pattern.</summary>
    [HttpPut("{id:int}/teams/{teamId:int}/roster")]
    public async Task<IActionResult> SaveRoster(int id, int teamId, ScheduleRosterInput input, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.SaveRosterAsync(id, teamId, input, cancellationToken));

    /// <summary>Sets who is in the team; anyone left out goes back to the everyone-team.</summary>
    [HttpPut("{id:int}/teams/{teamId:int}/members")]
    public async Task<IActionResult> SetTeamMembers(int id, int teamId, ScheduleMembersInput input, CancellationToken cancellationToken) =>
        NoContentOrFailure(await _schedules.SetTeamMembersAsync(id, teamId, input, cancellationToken));

    private ActionResult<CreatedResponse> Created(SaveResult result) =>
        result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);

    private IActionResult NoContentOrFailure(SaveResult result) =>
        result.Succeeded ? NoContent() : Failure(result);

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
