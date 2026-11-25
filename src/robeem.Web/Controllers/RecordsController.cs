using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
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

        var result = await ListPageAsync(chosen, from, to, employeeId, onlyWithExceptions, Math.Max(page, 1), size, field, descending, cancellationToken);
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
    /// Calculates the days already held again from a date to today (of the employee the list is filtered to, else of everyone), for when the
    /// policy, the rules, a holiday or a leave changed. Only what comes out different is rewritten; closed pay periods are left alone.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReprocessSince(DateTime? from, int? employeeId, string? status, bool onlyWithExceptions, int pageSize, CancellationToken cancellationToken)
    {
        DateTime? continueFrom = from;
        if (from is null)
        {
            TempData["ErrorMessage"] = "Choose the first day to calculate again, then press Calculate again.";
        }
        else
        {
            var result = await _records.ReprocessSinceAsync(from.Value, employeeId, cancellationToken);
            TempData["StatusMessage"] = ReprocessSinceMessage(result);

            // When one request did not reach every day, the list and the date box move on to where to carry on from.
            continueFrom = result.NextFrom ?? from;
        }

        return RedirectToAction(nameof(Index), new
        {
            from = continueFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
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
            days = (await _records.ListAsync(start, end, employee, null, false, 1, 200, RecordSortField.Date, false, null, null, cancellationToken)).Items;
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

    /// <summary>
    /// One day, with how it was calculated. When it was opened from the Attendance list, <paramref name="list"/> is that list (filters, sort, page size) and
    /// the day's place in it, and the page offers the day that follows.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Day(int employeeId, DateTime date, CancellationToken cancellationToken, RecordListContext? list = null)
    {
        var day = await _records.GetDayAsync(employeeId, date, cancellationToken);
        if (day is null)
        {
            return NotFound();
        }

        var fromList = list is { Position: > 0 } ? list : null;
        var (next, nextPosition) = fromList is null ? (null, 0) : await FindNextAsync(fromList, employeeId, date, cancellationToken);
        return View(new RecordDayViewModel
        {
            Day = day,
            CanReprocess = _actor.IsInRole(NavigationMenu.Hr),
            List = fromList,
            Next = next,
            NextPosition = nextPosition,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Reprocess(int employeeId, DateTime date, CancellationToken cancellationToken, RecordListContext? list = null)
    {
        var result = await _records.ReprocessAsync(employeeId, date, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = result.RecordsWritten + result.RecordsRetired + result.ExceptionsRaised + result.ExceptionsResolved == 0
            ? "The day was calculated again and nothing changed."
            : $"The day was calculated again: {result.RecordsWritten} record(s) written, {result.RecordsRetired} retired, {result.ExceptionsRaised} exception(s) raised, {result.ExceptionsResolved} resolved.";

        // Back to the day, still opened from the list it came from.
        var values = list is { Position: > 0 } ? list.ToDayValues(list.Position).ToDictionary(v => v.Key, v => (object?)v.Value) : new Dictionary<string, object?>();
        values["employeeId"] = employeeId;
        values["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return RedirectToAction(nameof(Day), values);
    }

    private static string ReprocessSinceMessage(ReprocessSinceResponse result)
    {
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"Calculated again from {result.From:yyyy-MM-dd} to {result.To:yyyy-MM-dd}: {result.DaysExamined} day(s) looked at, {result.DaysChanged} changed.");
        if (result.DaysChanged > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $" {result.RecordsWritten} record(s) written, {result.RecordsRetired} retired, {result.RecordsSentToReview} sent back to review; {result.ExceptionsRaised} exception(s) raised, {result.ExceptionsResolved} closed because the problem is gone.");
        }

        if (result.DaysInClosedPeriods > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $" {result.DaysInClosedPeriods} day(s) in closed pay periods were left alone.");
        }

        if (result.DaysFailed > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $" {result.DaysFailed} could not be calculated: press the button again to try them again.");
        }

        if (result.NextFrom is { } next)
        {
            message.Append(CultureInfo.InvariantCulture, $" There are more days: the first day below is {next:yyyy-MM-dd}, where to carry on; press the button again.");
        }

        return message.ToString();
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

    /// <summary>
    /// Asks the Api for one page of the Attendance list. The Status filter has choices the Api has no status for, which are translated here, so that
    /// the list and the day's page read the same days.
    /// </summary>
    private Task<PagedResult<RecordDayListItem>> ListPageAsync(string chosen, DateTime? from, DateTime? to, int? employeeId, bool onlyWithExceptions, int page, int size, RecordSortField field, bool descending, CancellationToken cancellationToken)
    {
        // "Open exceptions" is how the list names a day that waits for its exceptions; to the Api it is the days with an exception that is not closed.
        var openExceptions = chosen == RecordsIndexViewModel.OpenExceptions;

        // "Under timed" is an approved day that worked less than scheduled: to the Api, status Approved with the under-time filter on. "Approved" leaves those
        // out, since the list shows them under their own name.
        var underTimed = chosen == RecordsIndexViewModel.UnderTimed;
        var apiStatus = chosen is "Any" or RecordsIndexViewModel.OpenExceptions or RecordsIndexViewModel.Inferred ? null : underTimed ? "Approved" : chosen;
        bool? underTime = underTimed ? true : chosen == "Approved" ? false : null;

        // "Inferred" is not a status either: it is the days marked with a dot, whatever their status, and the Api is asked for those.
        bool? inferred = chosen == RecordsIndexViewModel.Inferred ? true : null;
        return _records.ListAsync(from, to, employeeId, apiStatus, onlyWithExceptions || openExceptions, page, size, field, descending, underTime, inferred, cancellationToken);
    }

    /// <summary>
    /// Finds the day after the given one in the list it was opened from. The list is read again, so it is as it is now: when the day has left it (its
    /// exceptions were settled under a filter of open exceptions), the day that moved up into its place is the next one. Nothing is returned at the end of the list.
    /// </summary>
    private async Task<(RecordDayListItem? Item, int Position)> FindNextAsync(RecordListContext list, int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        var chosen = RecordsIndexViewModel.Statuses.Contains(list.Status ?? string.Empty) ? list.Status! : "Any";
        var size = RecordsIndexViewModel.PageSizes.Contains(list.PageSize) ? list.PageSize : RecordsIndexViewModel.DefaultPageSize;
        var field = Enum.TryParse<RecordSortField>(list.Sort, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : RecordSortField.Date;
        var descending = string.Equals(list.Dir, "desc", StringComparison.OrdinalIgnoreCase);

        // The page that holds a place, read at most once for each page (the place and the one after it are usually on the same page).
        var pages = new Dictionary<int, PagedResult<RecordDayListItem>>();
        async Task<RecordDayListItem?> ItemAtAsync(int position)
        {
            var page = ((position - 1) / size) + 1;
            if (!pages.TryGetValue(page, out var result))
            {
                result = await ListPageAsync(chosen, list.From, list.To, list.FilterEmployeeId, list.OnlyWithExceptions, page, size, field, descending, cancellationToken);
                pages[page] = result;
            }

            // A page past the end is answered with the last one: then the place is past the end, too.
            var row = (position - 1) % size;
            return result.Page == page && row < result.Items.Count ? result.Items[row] : null;
        }

        var here = await ItemAtAsync(list.Position);
        if (here is null)
        {
            return (null, 0);
        }

        var stillThere = here.EmployeeId == employeeId && here.AttendanceDate.Date == date.Date;
        return stillThere ? (await ItemAtAsync(list.Position + 1), list.Position + 1) : (here, list.Position);
    }
}
