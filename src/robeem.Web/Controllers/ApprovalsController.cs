using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Everything an approver has to decide, in one queue: the corrections employees proposed for their days, and the records the engine left
/// for a person (overtime that needs pre-approval, work outside any shift, an incomplete day). A manager decides their direct reports';
/// HR decides the rest. The paging, the filters and who sees what are done by robeem.Api.
/// </summary>
[RequiresEmployee(HrExempt = true)]
public sealed class ApprovalsController : Controller
{
    private readonly IApprovalsApiClient _approvals;

    public ApprovalsController(IApprovalsApiClient approvals)
    {
        _approvals = approvals;
    }

    /// <summary>The queue, a page at a time. <paramref name="type"/> is the kind filter and <paramref name="why"/> the review reason filter.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(int? employeeId, DateTime? from, DateTime? to, string? type, string? why, int page, int pageSize, CancellationToken cancellationToken)
    {
        var chosenType = ApprovalsQueueViewModel.Types.Contains(type ?? string.Empty) ? type : null;
        var chosenWhy = ApprovalsQueueViewModel.Reasons.Contains(why ?? string.Empty) ? why : null;
        var size = ApprovalsQueueViewModel.PageSizes.Contains(pageSize) ? pageSize : ApprovalsQueueViewModel.DefaultPageSize;
        var result = await _approvals.ListPendingAsync(employeeId, from, to, chosenType, chosenWhy, Math.Max(page, 1), size, cancellationToken);
        return View(new ApprovalsQueueViewModel
        {
            Items = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Employees = await _approvals.ListEmployeeOptionsAsync(cancellationToken),
            EmployeeId = employeeId,
            From = from,
            To = to,
            Type = chosenType,
            Why = chosenWhy,
        });
    }

    /// <summary>
    /// Approves or rejects one approval. <paramref name="awaiting"/> is the step the user takes (Manager or HR; the queue says which for
    /// each row). <paramref name="reason"/> is the reason for a review's decision; the rest of the parameters are the queue as the
    /// approver had it (filters and page), so it is shown again the same way.
    /// </summary>
    [HttpPost]
    [RequiresEmployee]
    public async Task<IActionResult> Decide(int id, bool approved, string? awaiting, string? reason, int? employeeId, DateTime? from, DateTime? to, string? type, string? why, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await _approvals.DecideAsync(
            id,
            new DecideApprovalRequest { Approved = approved, Reason = reason, IsHrDecision = string.Equals(awaiting, "HR", StringComparison.Ordinal) },
            cancellationToken);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.MessageText;
        }
        else
        {
            TempData["StatusMessage"] = result.Value!.Type == "Review"
                ? (approved ? "Approved." : "Rejected. The reason is on record.")
                : result.Value.Status switch
                {
                    "Applied" => "Approved: the edits were made and the day recalculated.",
                    "Rejected" => "Rejected: the employee can propose again.",
                    _ => "Your approval is recorded. The correction still waits for its next approver.",
                };
        }

        // Back to the queue as it was; a page that is now past the end is answered with the last one.
        return RedirectToAction(nameof(Index), new { employeeId, from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd"), type, why, page, pageSize });
    }
}
