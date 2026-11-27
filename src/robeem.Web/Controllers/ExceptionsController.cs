using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Web.Controllers;

/// <summary>
/// Attendance exceptions of the acting user, shown a day at a time: their own (and, for a manager, their reports'; for
/// HR, everyone's). The employee corrects a day from here: the Correct page shows every punch of it.
/// </summary>
[RequiresEmployee(HrExempt = true)]
public sealed class ExceptionsController : Controller
{
    private readonly IExceptionsApiClient _exceptions;
    private readonly ICorrectionsApiClient _corrections;
    private readonly ICurrentActor _actor;

    public ExceptionsController(IExceptionsApiClient exceptions, ICorrectionsApiClient corrections, ICurrentActor actor)
    {
        _exceptions = exceptions;
        _corrections = corrections;
        _actor = actor;
    }

    /// <summary>The exception groups, a page at a time. The paging, the filters and who sees what are done by robeem.Api.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? status, DateTime? from, DateTime? to, int? employeeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var chosen = ExceptionsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status! : "Active";
        var size = ExceptionsIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : ExceptionsIndexViewModel.DefaultPageSize;
        var result = await _exceptions.ListGroupsAsync(chosen, from, to, employeeId, Math.Max(page, 1), size, cancellationToken);
        return View(new ExceptionsIndexViewModel
        {
            Groups = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Employees = await _exceptions.ListEmployeeOptionsAsync(cancellationToken),
            Status = chosen,
            From = from,
            To = to,
            EmployeeId = employeeId,
        });
    }

    /// <summary>
    /// Every punch of one day, to put it right: change a punch's type, add a missing one, or ignore one that should not count. The user's own
    /// day, or, for their manager or HR, another employee's (<paramref name="employeeId"/>).
    /// </summary>
    [HttpGet]
    [RequiresEmployee]
    public async Task<IActionResult> Correct(DateTime date, int? employeeId, bool fromDay, CancellationToken cancellationToken, RecordListContext? list = null)
    {
        if (await _corrections.GetDayAsync(date, OtherThanSelf(employeeId), cancellationToken) is not { } day)
        {
            return NotFound();
        }

        var form = DayCorrectionForm.For(day);
        form.FromDay = fromDay;
        return View(new DayCorrectionViewModel { Day = day, Form = form, List = FromList(list) });
    }

    /// <summary>Sends the edits of the day for approval, all together, with one reason.</summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Correct(DayCorrectionForm form, CancellationToken cancellationToken, RecordListContext? list = null)
    {
        var employeeId = OtherThanSelf(form.EmployeeId);
        var result = await _corrections.ProposeDayAsync(
            new DayCorrectionRequest { EmployeeId = employeeId, Date = form.Date.Date, Justification = form.Justification ?? string.Empty, Edits = form.ToEdits() },
            cancellationToken);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = form.Outcome switch
            {
                DayCorrectionOutcome.AppliedAtOnce => "The corrections were applied and the day was calculated again.",
                DayCorrectionOutcome.AwaitsManager => "The corrections were sent for the employee. They now wait for the employee's manager to decide, all together.",
                DayCorrectionOutcome.AwaitsHr => "The corrections were sent for the employee. They now wait for HR to decide, all together.",
                _ => "Your corrections were sent. They now wait for the approver's decision, all together.",
            };
            return form.FromDay && (employeeId ?? _actor.Effective?.EmployeeId) is { } dayEmployeeId
                ? RedirectToDay(dayEmployeeId, form.Date, FromList(list))
                : RedirectToAction(nameof(Index));
        }

        // Back to the page with what was typed, and why it was refused.
        if (await _corrections.GetDayAsync(form.Date.Date, employeeId, cancellationToken) is not { } day)
        {
            return NotFound();
        }

        foreach (var message in result.Messages)
        {
            ModelState.AddModelError(string.Empty, message);
        }

        return View(new DayCorrectionViewModel { Day = day, Form = form.WithPunchesOf(day), List = FromList(list) });
    }

    /// <summary>
    /// A manager or HR accepts a whole group of exceptions as it is, with one reason. The reason is required; the Api checks the user may. The
    /// other parameters are the list as the approver had it (filters and page), so it is shown again the same way.
    /// </summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> AcceptGroup(int groupId, string? reason, string? status, DateTime? from, DateTime? to, int? employeeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await _exceptions.AcceptGroupAsync(
            groupId,
            new AcceptExceptionRequest { Reason = reason ?? string.Empty, IsHrDecision = _actor.IsInRole(NavigationMenu.Hr) },
            cancellationToken);

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The exceptions were accepted. The day was calculated again."
            : result.MessageText;

        // Back to the list as it was; a page that is now past the end is answered with the last one.
        return RedirectToAction(nameof(Index), new { status, employeeId, from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd"), page, pageSize });
    }

    /// <summary>A manager or HR accepts a break exception as it is. The reason is required; the Api checks the user may.</summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Accept(int exceptionId, string? reason, CancellationToken cancellationToken, int? dayEmployeeId = null, DateTime? dayDate = null, RecordListContext? list = null)
    {
        var result = await _exceptions.AcceptAsync(
            exceptionId,
            new AcceptExceptionRequest { Reason = reason ?? string.Empty, IsHrDecision = _actor.IsInRole(NavigationMenu.Hr) },
            cancellationToken);

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The exception was accepted. The day was calculated again."
            : result.MessageText;

        // Accepted from a day's own page: back to that day, still opened from the list it came from.
        return dayEmployeeId is { } employeeId && dayDate is { } date
            ? RedirectToDay(employeeId, date, list)
            : RedirectToAction(nameof(Index));
    }

    /// <summary>The list a day was opened from, only when it says where the day sat in it: otherwise there is no list to carry.</summary>
    private static RecordListContext? FromList(RecordListContext? list) => list is { Position: > 0 } ? list : null;

    /// <summary>The employee to name to the Api: null for the acting user's own day, so that a day is only ever "on behalf" when it is someone else's.</summary>
    private int? OtherThanSelf(int? employeeId) => employeeId is { } id && id != _actor.Effective?.EmployeeId ? id : null;

    private RedirectToActionResult RedirectToDay(int employeeId, DateTime date, RecordListContext? list = null)
    {
        var values = list is { Position: > 0 } ? list.ToDayValues(list.Position).ToDictionary(v => v.Key, v => (object?)v.Value) : new Dictionary<string, object?>();
        values["employeeId"] = employeeId;
        values["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return RedirectToAction("Day", "Records", values);
    }
}
