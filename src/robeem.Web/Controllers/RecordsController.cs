using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
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
[RequiresEmployee(HrExempt = true)]
public sealed class RecordsController : Controller
{
    private readonly IRecordsApiClient _records;
    private readonly IExceptionsApiClient _exceptions;
    private readonly ICurrentActor _actor;

    public RecordsController(IRecordsApiClient records, IExceptionsApiClient exceptions, ICurrentActor actor)
    {
        _records = records;
        _exceptions = exceptions;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, DateTime? from, DateTime? to, bool onlyWithExceptions, int? employeeId, int page, int pageSize, string? sort, string? dir, CancellationToken cancellationToken)
    {
        var chosen = RecordsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status! : "Any";
        var size = RecordsIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : RecordsIndexViewModel.DefaultPageSize;

        // Oldest first unless a known column and direction are asked for.
        var field = Enum.TryParse<RecordSortField>(sort, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : RecordSortField.Date;
        var descending = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var result = await _records.ListAsync(from, to, employeeId, chosen == "Any" ? null : chosen, onlyWithExceptions, Math.Max(page, 1), size, field, descending, cancellationToken);
        return View(new RecordsIndexViewModel
        {
            Items = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Employees = await _records.ListEmployeeOptionsAsync(cancellationToken),
            EmployeeId = employeeId,
            Status = chosen,
            From = from,
            To = to,
            OnlyWithExceptions = onlyWithExceptions,
            Sort = field,
            Descending = descending,
        });
    }

    /// <summary>
    /// One employee's days and exceptions over a date range (the last 30 days unless asked otherwise, at most 92), with the totals.
    /// The employee must be one the user may see; without one the page only offers the choice.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Employee(int? employeeId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var employees = await _records.ListEmployeeOptionsAsync(cancellationToken);
        var end = (to ?? DateTime.Today).Date;
        var start = (from ?? end.AddDays(1 - EmployeeAttendanceViewModel.DefaultRangeDays)).Date;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var shortened = (end - start).TotalDays >= EmployeeAttendanceViewModel.MaxRangeDays;
        if (shortened)
        {
            start = end.AddDays(1 - EmployeeAttendanceViewModel.MaxRangeDays);
        }

        // A person with a single choice (an employee, who sees only themselves) does not have to make it.
        var chosen = employeeId is { } id && employees.Any(e => e.Id == id) ? id : employees.Count == 1 ? employees[0].Id : (int?)null;
        IReadOnlyList<RecordDayListItem> days = Array.Empty<RecordDayListItem>();
        IReadOnlyList<ExceptionListItem> exceptions = Array.Empty<ExceptionListItem>();
        if (chosen is { } employee)
        {
            // The range is short enough for every day to fit on one page of the largest size.
            days = (await _records.ListAsync(start, end, employee, null, false, 1, 200, RecordSortField.Date, false, cancellationToken)).Items;
            exceptions = (await _exceptions.ListAsync("All", start, end, employee, 200, cancellationToken)).OrderBy(e => e.AttendanceDate).ThenBy(e => e.Id).ToList();
        }

        return View(new EmployeeAttendanceViewModel
        {
            Employees = employees,
            EmployeeId = chosen,
            From = start,
            To = end,
            RangeShortened = shortened,
            Days = days,
            Exceptions = exceptions,
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
