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
[RequiresEmployee(HrExempt = true)]
public sealed class ReviewsController : Controller
{
    private readonly IReviewsApiClient _reviews;
    private readonly ICurrentActor _actor;

    public ReviewsController(IReviewsApiClient reviews, ICurrentActor actor)
    {
        _reviews = reviews;
        _actor = actor;
    }

    /// <summary>The queue, a page at a time (the paging and the filters are done by robeem.Api). <paramref name="why"/> is the reason filter.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(int? employeeId, DateTime? from, DateTime? to, string? why, int page, int pageSize, CancellationToken cancellationToken)
    {
        var chosenWhy = ReviewsQueueViewModel.Reasons.Contains(why ?? string.Empty) ? why : null;
        var size = ReviewsQueueViewModel.PageSizes.Contains(pageSize) ? pageSize : ReviewsQueueViewModel.DefaultPageSize;
        var result = await _reviews.ListPendingAsync(employeeId, from, to, chosenWhy, Math.Max(page, 1), size, cancellationToken);
        return View(new ReviewsQueueViewModel
        {
            Items = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Employees = await _reviews.ListEmployeeOptionsAsync(cancellationToken),
            EmployeeId = employeeId,
            From = from,
            To = to,
            Why = chosenWhy,
        });
    }

    /// <summary>
    /// Approves or rejects one record. <paramref name="reason"/> is the reason for the decision; the rest of the parameters are the
    /// queue as the reviewer had it (filters and page), so it is shown again the same way.
    /// </summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Decide(int recordId, bool approved, string? reason, int? employeeId, DateTime? from, DateTime? to, string? why, int page, int pageSize, CancellationToken cancellationToken)
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

        // Back to the queue as it was; a page that is now past the end is answered with the last one.
        return RedirectToAction(nameof(Index), new { employeeId, from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd"), why, page, pageSize });
    }
}
