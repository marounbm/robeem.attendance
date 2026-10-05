using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Pay periods: the dates payroll pays for. Closing a period ends the payroll extraction of its
/// days; reopening one needs a reason, which is kept in the audit log.
/// </summary>
public sealed class PayPeriodsController : Controller
{
    private readonly IPayPeriodsApiClient _periods;

    public PayPeriodsController(IPayPeriodsApiClient periods)
    {
        _periods = periods;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var periods = await _periods.ListAsync(cancellationToken);
        return View(new PayPeriodsIndexViewModel { Periods = periods, Input = NextPeriod(periods) });
    }

    [HttpPost]
    public async Task<IActionResult> Create(PayPeriodsIndexViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _periods.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Pay period {model.Input.StartDate:yyyy-MM-dd} to {model.Input.EndDate:yyyy-MM-dd} was created.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(PayPeriodsIndexViewModel.Input)}.{field}", message);
            }
        }

        model.Periods = await _periods.ListAsync(cancellationToken);
        return View(nameof(Index), model);
    }

    [HttpPost]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken) =>
        Done(await _periods.CloseAsync(id, cancellationToken), "The pay period was closed: payroll no longer receives its days.");

    [HttpPost]
    public async Task<IActionResult> Reopen(int id, string? reason, CancellationToken cancellationToken)
    {
        // The Api refuses an empty reason too; checking here saves the round trip and words it for the form.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Give a reason to reopen a closed pay period.";
            return RedirectToAction(nameof(Index));
        }

        return Done(await _periods.ReopenAsync(id, reason, cancellationToken), "The pay period was reopened. The reason is in the audit log.");
    }

    /// <summary>The first day after the latest period, running to the end of that month's length; this month when there is none.</summary>
    private static PayPeriodInput NextPeriod(IReadOnlyList<PayPeriodItem> periods)
    {
        var start = periods.Count == 0
            ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
            : periods.Max(p => p.EndDate).Date.AddDays(1);
        return new PayPeriodInput { StartDate = start, EndDate = start.AddMonths(1).AddDays(-1) };
    }

    private IActionResult Done(ApiSaveResult result, string success)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? success : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
