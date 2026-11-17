using System.Globalization;
using System.Text;
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
    private readonly IAttendanceSettingsApiClient _settings;

    public RecordsController(IRecordsApiClient records, IExceptionsApiClient exceptions, ICurrentActor actor, IAttendanceSettingsApiClient settings)
    {
        _records = records;
        _exceptions = exceptions;
        _actor = actor;
        _settings = settings;
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
        var canCheckMissing = _actor.IsInRole(NavigationMenu.Hr);
        var (checkFrom, checkTo) = canCheckMissing ? await DefaultPeriodAsync(cancellationToken) : (null, null);
        return View(new RecordsIndexViewModel
        {
            CheckFrom = checkFrom,
            CheckTo = checkTo,
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
            CanCheckMissing = canCheckMissing,
        });
    }

    /// <summary>
    /// Looks for the days nobody punched on in the period the list is filtered to (of the employee it is filtered to, else of everyone), so that a
    /// no-show is raised for each scheduled day that was missed. A day is calculated when punches arrive for it, and the nightly run reaches only
    /// the last days, so this is how a period imported later is checked.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CheckMissing(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int pageSize, CancellationToken cancellationToken)
    {
        // An empty first day starts at the date the punching started; an empty last day ends at the last day attendance was entered for.
        if (from is null || to is null)
        {
            var (defaultFrom, defaultTo) = await DefaultPeriodAsync(cancellationToken);
            from ??= defaultFrom;
            to ??= defaultTo;
        }

        string? problem = null;
        if (from is null || to is null)
        {
            problem = "Choose the first and the last day to look at, then press Find missing days.";
        }
        else if (to < from)
        {
            problem = "The last day can't be before the first.";
        }
        else if ((to.Value.Date - from.Value.Date).Days + 1 > CheckMissingDaysRequest.MaxDays)
        {
            problem = $"Look at at most {CheckMissingDaysRequest.MaxDays} days at a time.";
        }

        if (problem is not null)
        {
            TempData["ErrorMessage"] = problem;
        }
        else
        {
            var result = await _records.CheckMissingDaysAsync(from!.Value, to!.Value, employeeId, cancellationToken);
            TempData["StatusMessage"] = CheckMissingMessage(result);
        }

        return RedirectToAction(nameof(Index), new
        {
            from = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            employeeId,
            status,
            onlyWithExceptions = onlyWithExceptions ? "true" : null,
            pageSize = pageSize > 0 ? pageSize : (int?)null,
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

    private static string CheckMissingMessage(CheckMissingDaysResponse result)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"Looked for missing days from {result.From:yyyy-MM-dd} to {result.To:yyyy-MM-dd}: {result.DaysCalculated} day(s) calculated.");
        if (result.DaysFailed > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $" {result.DaysFailed} could not be calculated and are tried again by the nightly run.");
        }

        if (result.DaysDeferred > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $" {result.DaysDeferred} more are waiting: press the button again.");
        }

        if (result.DatesWithoutData.Count > 0)
        {
            var shown = string.Join(", ", result.DatesWithoutData.Take(5).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            message.Append(CultureInfo.InvariantCulture, $" Nobody punched at all on {result.DatesWithoutData.Count} working date(s) ({shown}{(result.DatesWithoutData.Count > 5 ? ", …" : string.Empty)}); those are skipped, because the import is more likely to be missing than everyone away.");
        }

        message.Append(" The no-shows are on the Exceptions tab.");
        return message.ToString();
    }

    /// <summary>
    /// The period looked at when none is chosen: from the date the punching started to the last day attendance was entered for
    /// (never later than yesterday, the last day that can be missing). Either is null when the system does not know it.
    /// </summary>
    private async Task<(DateTime? From, DateTime? To)> DefaultPeriodAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken);
        var yesterday = DateTime.Today.AddDays(-1);
        var to = settings.LastAttendanceDate is { } last ? (last.Date > yesterday ? yesterday : last.Date) : (DateTime?)null;
        return (settings.StartDate?.Date, to);
    }
}
