using Microsoft.AspNetCore.Mvc;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Web.Controllers;

/// <summary>
/// Calculated attendance, day by day: your own days, a manager's direct reports', HR's everyone's.
/// A day opens to show how its hours were worked out, from the punches to the review decisions;
/// HR can run a day through the processing engine again.
/// </summary>
public sealed class RecordsController : Controller
{
    private const int PageSize = 200;

    private readonly IRecordsApiClient _records;
    private readonly ICurrentActor _actor;

    public RecordsController(IRecordsApiClient records, ICurrentActor actor)
    {
        _records = records;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, DateTime? from, DateTime? to, bool onlyWithExceptions, int? employeeId, CancellationToken cancellationToken)
    {
        var chosen = RecordsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status! : "Any";
        return View(new RecordsIndexViewModel
        {
            Items = await _records.ListAsync(from, to, employeeId, chosen == "Any" ? null : chosen, onlyWithExceptions, PageSize, cancellationToken),
            EmployeeId = employeeId,
            Status = chosen,
            From = from,
            To = to,
            OnlyWithExceptions = onlyWithExceptions,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Day(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        var day = await _records.GetDayAsync(employeeId, date, cancellationToken);
        return day is null
            ? NotFound()
            : View(new RecordDayViewModel { Day = day, CanReprocess = _actor.IsInRole(NavigationMenu.Hr) });
    }

    [HttpPost]
    public async Task<IActionResult> Reprocess(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        var result = await _records.ReprocessAsync(employeeId, date, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = result.RecordsWritten + result.RecordsRetired + result.ExceptionsRaised + result.ExceptionsResolved == 0
            ? "The day was calculated again and nothing changed."
            : $"The day was calculated again: {result.RecordsWritten} record(s) written, {result.RecordsRetired} retired, {result.ExceptionsRaised} exception(s) raised, {result.ExceptionsResolved} resolved.";
        return RedirectToAction(nameof(Day), new { employeeId, date = date.ToString("yyyy-MM-dd") });
    }
}
