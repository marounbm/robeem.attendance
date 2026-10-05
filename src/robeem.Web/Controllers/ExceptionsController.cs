using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Attendance exceptions of the acting user: their own (and, for a manager, their reports'; for
/// HR, everyone's). The exception's own employee proposes the missing punch from here.
/// </summary>
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
    public async Task<IActionResult> Propose(int exceptionId, DateTime proposedValue, string? justification, CancellationToken cancellationToken)
    {
        var result = await _corrections.ProposeAsync(
            new ProposeCorrectionRequest { ExceptionId = exceptionId, ProposedValue = proposedValue, Justification = justification ?? string.Empty },
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
