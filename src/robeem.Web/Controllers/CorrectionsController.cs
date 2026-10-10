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
    [RequiresEmployee(HrExempt = true)]
    public async Task<IActionResult> Index(CorrectionApprover? awaiting, CancellationToken cancellationToken)
    {
        // The Manager queue is the actor's direct reports', so the Api refuses it to one with no employee of
        // their own (the ActorAccessFilter has already turned away one who is not HR either). The HR queue
        // needs none. The actor is the "act as" choice or else the Api's stand-in user; if the Api could not
        // say who that is, the Api decides.
        var known = _actor.Effective;
        var canManager = _actor.IsInRole(NavigationMenu.Manager) && (known is null || known.EmployeeId is not null);
        var canHr = _actor.IsInRole(NavigationMenu.Hr);

        // The tab asked for, if the acting roles allow it; otherwise the first they do. When it is not known
        // whether they have an employee, the HR queue is the safer default for one holding both.
        var tab = awaiting is { } asked && ((asked == CorrectionApprover.Manager && canManager) || (asked == CorrectionApprover.HR && canHr))
            ? asked
            : canManager && (known is not null || !canHr) ? CorrectionApprover.Manager : CorrectionApprover.HR;

        return View(new CorrectionsQueueViewModel
        {
            Awaiting = tab,
            CanManager = canManager,
            CanHr = canHr,
            Items = await _corrections.ListPendingAsync(tab, cancellationToken),
        });
    }

    [HttpPost]
    [RequiresEmployee]
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
