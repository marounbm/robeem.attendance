using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The attendance settings of the tenant: the date the punching started. Nothing is expected, accepted or looked for before it.
/// </summary>
public sealed class AttendanceSettingsController : Controller
{
    private readonly IAttendanceSettingsApiClient _settings;

    public AttendanceSettingsController(IAttendanceSettingsApiClient settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var current = await _settings.GetAsync(cancellationToken);
        return View(new AttendanceSettingsViewModel { Input = new() { StartDate = current.StartDate }, LastAttendanceDate = current.LastAttendanceDate });
    }

    [HttpPost]
    public async Task<IActionResult> Save(AttendanceSettingsViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _settings.SaveAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = model.Input.StartDate is { } date
                    ? $"The punching started on {date:yyyy-MM-dd}. Nothing before it is expected."
                    : "The start date was cleared.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(AttendanceSettingsViewModel.Input)}.{field}", message);
            }
        }

        model.LastAttendanceDate = (await _settings.GetAsync(cancellationToken)).LastAttendanceDate;
        return View(nameof(Index), model);
    }
}
