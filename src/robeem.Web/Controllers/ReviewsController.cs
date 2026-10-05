using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Web.Controllers;

/// <summary>
/// Records the engine left for a person: overtime that needs pre-approval, work outside any
/// shift, an incomplete day. A manager reviews their direct reports; HR anyone. Only an approved
/// record (and, for overtime under a pre-approval policy, an approving decision) reaches payroll.
/// </summary>
public sealed class ReviewsController : Controller
{
    private readonly IReviewsApiClient _reviews;
    private readonly ICurrentActor _actor;

    public ReviewsController(IReviewsApiClient reviews, ICurrentActor actor)
    {
        _reviews = reviews;
        _actor = actor;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new ReviewsQueueViewModel { Items = await _reviews.ListPendingAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Decide(int recordId, bool approved, string? reason, CancellationToken cancellationToken)
    {
        // A user who is both manager and HR reviews as HR, who may review anyone.
        var result = await _reviews.DecideAsync(
            recordId,
            new DecideReviewRequest { Approved = approved, Reason = reason, IsHrDecision = _actor.IsInRole(NavigationMenu.Hr) },
            cancellationToken);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = approved ? "Approved." : "Rejected. The reason is on record.";
        }
        else
        {
            TempData["ErrorMessage"] = result.MessageText;
        }

        return RedirectToAction(nameof(Index));
    }
}
