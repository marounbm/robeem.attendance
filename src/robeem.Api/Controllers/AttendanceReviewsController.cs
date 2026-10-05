using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The review of attendance records the engine left for a person: overtime that needs
/// pre-approval, unscheduled work, incomplete days. A manager reviews their direct reports, HR
/// anyone. Who decides is the authenticated user.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/reviews")]
public sealed class AttendanceReviewsController : ControllerBase
{
    private readonly IAttendanceReviewService _reviews;
    private readonly ICurrentUser _user;

    public AttendanceReviewsController(IAttendanceReviewService reviews, ICurrentUser user)
    {
        _reviews = reviews;
        _user = user;
    }

    /// <summary>The records waiting for the user's review: their direct reports', or every employee's for HR.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<IReadOnlyList<ReviewListItem>>> ListPending(CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } approverId)
        {
            return NoEmployeeIdentity();
        }

        return Ok(await _reviews.ListPendingAsync(approverId, _user.IsInRole(AttendanceRoles.HRAdministrator), cancellationToken));
    }

    /// <summary>A manager or HR user approves or rejects a record waiting for review.</summary>
    [HttpPost("{recordId:int}/decision")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<ReviewDecisionResponse>> Decide(int recordId, DecideReviewRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } decidedBy)
        {
            return NoEmployeeIdentity();
        }

        // A user with one of the two roles decides in it; one with both says which.
        var isManager = _user.IsInRole(AttendanceRoles.Manager);
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        var asHr = isManager && isHr ? request.IsHrDecision : isHr;

        var result = await _reviews.DecideAsync(recordId, decidedBy, asHr, request.Approved, request.Reason, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field, message);
            }

            return ValidationProblem(ModelState);
        }

        return new ReviewDecisionResponse
        {
            DecisionId = result.DecisionId!.Value,
            RecordId = recordId,
            Outcome = result.Outcome!.Value.ToString(),
            RecordStatus = result.RecordStatus!.Value.ToString(),
        };
    }

    private ObjectResult NoEmployeeIdentity() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim).");
}
