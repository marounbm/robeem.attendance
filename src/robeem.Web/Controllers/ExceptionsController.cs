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
    private const int PageSize = 200;

    private readonly IExceptionsApiClient _exceptions;
    private readonly ICorrectionsApiClient _corrections;
    private readonly ICurrentActor _actor;

    public ExceptionsController(IExceptionsApiClient exceptions, ICorrectionsApiClient corrections, ICurrentActor actor)
    {
        _exceptions = exceptions;
        _corrections = corrections;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var chosen = ExceptionsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status! : "Active";
        return View(new ExceptionsIndexViewModel
        {
            Items = await _exceptions.ListAsync(chosen, from, to, null, PageSize, cancellationToken),
            Status = chosen,
            From = from,
            To = to,
        });
    }

    /// <summary>
    /// Every punch of one of the employee's days, to put it right: change a punch's type, add a missing one, or ignore one that should not count.
    /// </summary>
    [HttpGet]
    [RequiresEmployee]
    public async Task<IActionResult> Correct(DateTime date, CancellationToken cancellationToken)
    {
        if (await _corrections.GetDayAsync(date, cancellationToken) is not { } day)
        {
            return NotFound();
        }

        return View(new DayCorrectionViewModel { Day = day, Form = DayCorrectionForm.For(day) });
    }

    /// <summary>Sends the edits of the day for approval, all together, with one reason.</summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Correct(DayCorrectionForm form, CancellationToken cancellationToken)
    {
        var result = await _corrections.ProposeDayAsync(
            new DayCorrectionRequest { Date = form.Date.Date, Justification = form.Justification ?? string.Empty, Edits = form.ToEdits() },
            cancellationToken);
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Your corrections were sent. They now wait for the approver's decision, all together.";
            return RedirectToAction(nameof(Index));
        }

        // Back to the page with what was typed, and why it was refused.
        if (await _corrections.GetDayAsync(form.Date.Date, cancellationToken) is not { } day)
        {
            return NotFound();
        }

        foreach (var message in result.Messages)
        {
            ModelState.AddModelError(string.Empty, message);
        }

        return View(new DayCorrectionViewModel { Day = day, Form = form.WithPunchesOf(day) });
    }

    /// <summary>A manager or HR accepts a break exception as it is. The reason is required; the Api checks the user may.</summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Accept(int exceptionId, string? reason, CancellationToken cancellationToken)
    {
        var result = await _exceptions.AcceptAsync(
            exceptionId,
            new AcceptExceptionRequest { Reason = reason ?? string.Empty, IsHrDecision = _actor.IsInRole(NavigationMenu.Hr) },
            cancellationToken);

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The exception was accepted. The day was calculated again."
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
