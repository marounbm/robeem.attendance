using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.HR.Schedules;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Defining time schedules: the repeating pattern, its shifts, its teams and the roster each team
/// works. Every day of attendance is calculated against these, so a change applies to days
/// processed from then on.
/// </summary>
public sealed class TimeSchedulesController : Controller
{
    private readonly ITimeSchedulesApiClient _schedules;

    public TimeSchedulesController(ITimeSchedulesApiClient schedules)
    {
        _schedules = schedules;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new TimeSchedulesIndexViewModel { Schedules = await _schedules.ListAsync(cancellationToken) });

    [HttpGet]
    public IActionResult Create() => View(new TimeScheduleFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(TimeScheduleFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _schedules.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Time schedule '{model.Input.Title}' was created with its team for everyone. Add its shifts, then set the roster.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }

            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(TimeScheduleFormViewModel.Input)}.{field}", message);
            }
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        await _schedules.GetAsync(id, cancellationToken) is { } schedule
            ? View(new TimeScheduleDetailsViewModel { Schedule = schedule })
            : NotFound();

    [HttpPost]
    public async Task<IActionResult> UpdateSettings(int id, TimeScheduleInput input, CancellationToken cancellationToken) =>
        Done(id, ModelState.IsValid ? await _schedules.UpdateAsync(id, input, cancellationToken) : Invalid(), "The schedule's settings were saved.");

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _schedules.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.MessageText;
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["StatusMessage"] = "The time schedule was deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddShift(int id, ScheduleShiftInput input, CancellationToken cancellationToken) =>
        Done(id, ModelState.IsValid ? await _schedules.AddShiftAsync(id, input, cancellationToken) : Invalid(), $"Shift '{input.Title}' was added.");

    [HttpPost]
    public async Task<IActionResult> UpdateShift(int id, int shiftId, ScheduleShiftInput input, CancellationToken cancellationToken) =>
        Done(id, ModelState.IsValid ? await _schedules.UpdateShiftAsync(id, shiftId, input, cancellationToken) : Invalid(), $"Shift '{input.Title}' was saved.");

    [HttpPost]
    public async Task<IActionResult> DeleteShift(int id, int shiftId, CancellationToken cancellationToken) =>
        Done(id, await _schedules.DeleteShiftAsync(id, shiftId, cancellationToken), "The shift was deleted.");

    [HttpPost]
    public async Task<IActionResult> AddTeam(int id, ScheduleTeamInput input, CancellationToken cancellationToken) =>
        Done(id, ModelState.IsValid ? await _schedules.AddTeamAsync(id, input, cancellationToken) : Invalid(), $"Team '{input.Title}' was added. Choose its members and set its roster.");

    [HttpPost]
    public async Task<IActionResult> RenameTeam(int id, int teamId, ScheduleTeamInput input, CancellationToken cancellationToken) =>
        Done(id, ModelState.IsValid ? await _schedules.RenameTeamAsync(id, teamId, input, cancellationToken) : Invalid(), "The team was renamed.");

    [HttpPost]
    public async Task<IActionResult> DeleteTeam(int id, int teamId, CancellationToken cancellationToken) =>
        Done(id, await _schedules.DeleteTeamAsync(id, teamId, cancellationToken), "The team was deleted; its members went back to the team for everyone.");

    /// <param name="cell">The ticked boxes of the roster grid, each <c>day:shiftId</c>; none means a team that never works.</param>
    [HttpPost]
    public async Task<IActionResult> SaveRoster(int id, int teamId, string[]? cell, CancellationToken cancellationToken)
    {
        var input = new ScheduleRosterInput();
        foreach (var text in cell ?? [])
        {
            var parts = text.Split(':');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var shift))
            {
                return Done(id, Invalid(), string.Empty);
            }

            input.Cells.Add(new ScheduleRosterCell { CycleDay = day, ShiftId = shift });
        }

        return Done(id, await _schedules.SaveRosterAsync(id, teamId, input, cancellationToken), "The roster was saved. Days already processed keep their result until they are calculated again.");
    }

    [HttpPost]
    public async Task<IActionResult> SetMembers(int id, int teamId, int[]? employeeId, CancellationToken cancellationToken) =>
        Done(id, await _schedules.SetTeamMembersAsync(id, teamId, new ScheduleMembersInput { EmployeeIds = [.. employeeId ?? []] }, cancellationToken), "The team's members were saved.");

    private static ApiSaveResult Invalid() => ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "Fill in every required field with a valid value." });

    private IActionResult Done(int id, ApiSaveResult result, string success)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? success : result.MessageText;
        return RedirectToAction(nameof(Details), new { id });
    }
}
