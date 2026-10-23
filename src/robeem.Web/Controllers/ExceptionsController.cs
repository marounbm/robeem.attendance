using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Attendance exceptions of the acting user: their own (and, for a manager, their reports'; for
/// HR, everyone's). The exception's own employee proposes the missing punch from here.
/// </summary>
[RequiresEmployee(HrExempt = true)]
public sealed class ExceptionsController : Controller
{
    private const int PageSize = 200;

    private readonly IExceptionsApiClient _exceptions;
    private readonly ICorrectionsApiClient _corrections;

    public ExceptionsController(IExceptionsApiClient exceptions, ICorrectionsApiClient corrections)
    {
        _exceptions = exceptions;
        _corrections = corrections;
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

    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Propose(int exceptionId, DateTime attendanceDate, TimeOnly? proposedTime, bool afterMidnight, string? justification, CancellationToken cancellationToken)
    {
        if (proposedTime is not { } time)
        {
            TempData["ErrorMessage"] = "Enter the time the punch happened, as hours and minutes.";
            return RedirectToAction(nameof(Index));
        }

        // The person enters a time only. The day is the exception's day, or the day after when they say the
        // punch was after midnight (only offered for a shift that ends then); the Api refuses any other day.
        var proposed = attendanceDate.Date.AddDays(afterMidnight ? 1 : 0).Add(time.ToTimeSpan());
        var result = await _corrections.ProposeAsync(
            new ProposeCorrectionRequest { ExceptionId = exceptionId, ProposedValue = proposed, Justification = justification ?? string.Empty },
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Correction proposed. It now waits for its approver's decision.";
        }
        else
        {
            TempData["ErrorMessage"] = result.MessageText;
        }

        return RedirectToAction(nameof(Index));
    }
}
