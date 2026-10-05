using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Web.Controllers;

/// <summary>
/// The approvers' queue of proposed corrections. A manager sees their direct reports'; HR sees
/// the HR-step ones and those of employees who have no manager. Approving applies the missing
/// punch once every approval the policy asks for is in.
/// </summary>
public sealed class CorrectionsController : Controller
{
    private readonly ICorrectionsApiClient _corrections;
    private readonly ICurrentActor _actor;

    public CorrectionsController(ICorrectionsApiClient corrections, ICurrentActor actor)
    {
        _corrections = corrections;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CorrectionApprover? awaiting, CancellationToken cancellationToken)
    {
        var canManager = _actor.IsInRole(NavigationMenu.Manager);
        var canHr = _actor.IsInRole(NavigationMenu.Hr);

        // The tab asked for, if the acting roles allow it; otherwise the first they do.
        var tab = awaiting is { } asked && ((asked == CorrectionApprover.Manager && canManager) || (asked == CorrectionApprover.HR && canHr))
            ? asked
            : canManager ? CorrectionApprover.Manager : CorrectionApprover.HR;

        return View(new CorrectionsQueueViewModel
        {
            Awaiting = tab,
            CanManager = canManager,
            CanHr = canHr,
            Items = await _corrections.ListPendingAsync(tab, cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Decide(int id, bool approved, CorrectionApprover awaiting, CancellationToken cancellationToken)
    {
        var result = await _corrections.DecideAsync(
            id,
            new DecideCorrectionRequest { Approved = approved, IsHrDecision = awaiting == CorrectionApprover.HR },
            cancellationToken);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.MessageText;
        }
        else
        {
            TempData["StatusMessage"] = result.Value!.Status switch
            {
                "Applied" => "Approved: the punch was added and the day recalculated.",
                "Rejected" => "Rejected: the employee can propose again.",
                _ => "Your approval is recorded. The correction still waits for its next approver.",
            };
        }

        return RedirectToAction(nameof(Index), new { awaiting });
    }
}
