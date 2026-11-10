using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.HR.Holidays;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>The tenant's public holidays: a year at a time, add, edit and remove.</summary>
public sealed class HolidaysController : Controller
{
    private const string StatusMessageKey = "StatusMessage";
    private const string ErrorMessageKey = "ErrorMessage";

    private readonly IHolidaysApiClient _holidays;

    public HolidaysController(IHolidaysApiClient holidays)
    {
        _holidays = holidays;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? year, CancellationToken cancellationToken)
    {
        var shown = year is > 0 and < 10000 ? year.Value : DateTime.Today.Year;
        return View(await BuildAsync(shown, new HolidayInput { Date = DefaultDate(shown) }, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(HolidaysIndexViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _holidays.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = $"{model.Input.Name.Trim()} was added.";
                return RedirectToAction(nameof(Index), new { year = model.Input.Date.Year });
            }

            AddErrors(result, nameof(HolidaysIndexViewModel.Input));
        }

        var shown = model.Input.Date.Year is > 0 and < 10000 ? model.Input.Date.Year : DateTime.Today.Year;
        return View(nameof(Index), await BuildAsync(shown, model.Input, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var holiday = await _holidays.GetAsync(id, cancellationToken);
        if (holiday is null)
        {
            return NotFound();
        }

        return View(new HolidayFormViewModel
        {
            Id = id,
            Input = new HolidayInput { Date = holiday.Date, Name = holiday.Name, IsProvisional = holiday.IsProvisional },
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, HolidayFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _holidays.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData[StatusMessageKey] = $"{model.Input.Name.Trim()} was updated.";
                return RedirectToAction(nameof(Index), new { year = model.Input.Date.Year });
            }

            AddErrors(result, nameof(HolidayFormViewModel.Input));
        }

        model.Id = id;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int id, int? year, CancellationToken cancellationToken)
    {
        var result = await _holidays.DeleteAsync(id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? StatusMessageKey : ErrorMessageKey] = result.Succeeded ? "The holiday was removed." : result.MessageText;
        return RedirectToAction(nameof(Index), new { year });
    }

    /// <summary>The first day of the year if it is not this year, otherwise today.</summary>
    private static DateTime DefaultDate(int year) => year == DateTime.Today.Year ? DateTime.Today : new DateTime(year, 1, 1);

    private async Task<HolidaysIndexViewModel> BuildAsync(int year, HolidayInput input, CancellationToken cancellationToken)
    {
        var years = (await _holidays.ListYearsAsync(cancellationToken)).Append(year).Append(DateTime.Today.Year).Append(DateTime.Today.Year + 1).Distinct().OrderBy(y => y).ToList();
        return new HolidaysIndexViewModel
        {
            Year = year,
            Years = years,
            Holidays = await _holidays.ListAsync(year, cancellationToken),
            Input = input,
        };
    }

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            // An empty field is a model-level error (shown in the validation summary).
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
