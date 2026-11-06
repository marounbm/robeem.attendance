using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Api.Controllers;

/// <summary>
/// What an approver has to decide, whatever it is: a correction an employee asked for, or a record the engine left for review.
/// A manager decides their direct reports', HR the rest. Who decides is the authenticated user. The older
/// <c>api/attendance/corrections</c> and <c>api/attendance/reviews</c> list and decision endpoints do the same for one kind each.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/approvals")]
public sealed class AttendanceApprovalsController : ControllerBase
{
    private readonly IAttendanceApprovalService _approvals;
    private readonly ICurrentUser _user;

    public AttendanceApprovalsController(IAttendanceApprovalService approvals, ICurrentUser user)
    {
        _approvals = approvals;
        _user = user;
    }

    /// <summary>The approvals waiting for the user's decision, oldest day first, a page at a time.</summary>
    /// <param name="employeeId">Only this employee (within what the user may decide).</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="type">Only this kind: Correction or Review.</param>
    /// <param name="reason">Only reviews waiting for this reason: Overtime, UnscheduledWork, IncompleteDay or Review.</param>
    /// <param name="page">The 1-based page (default 1). A page past the end answers the last page.</param>
    /// <param name="pageSize">Approvals per page (default 20, at most 200).</param>
    [HttpGet("pending")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<PagedResult<ApprovalListItem>>> ListPending(int? employeeId, DateTime? from, DateTime? to, string? type, string? reason, int page, int pageSize, CancellationToken cancellationToken)
    {
        // A manager's queue is their direct reports', so it needs to know who they are. HR's is everyone's,
        // and HR can look at it with no employee of their own (id 0 matches no employee).
        var isManager = _user.IsInRole(AttendanceRoles.Manager);
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        if (_user.EmployeeId is null && !isHr)
        {
            return NoEmployeeIdentity();
        }

        AttendanceApprovalType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<AttendanceApprovalType>(type.Trim(), ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
            {
                ModelState.AddModelError(nameof(type), $"The type must be one of: {string.Join(", ", Enum.GetNames<AttendanceApprovalType>())}.");
                return ValidationProblem(ModelState);
            }

            parsedType = kind;
        }

        ReviewReason? parsedReason = null;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            if (!Enum.TryParse<ReviewReason>(reason.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
            {
                ModelState.AddModelError(nameof(reason), $"The reason must be one of: {string.Join(", ", Enum.GetNames<ReviewReason>())}.");
                return ValidationProblem(ModelState);
            }

            parsedReason = value;
        }

        var filter = new ApprovalFilter(employeeId, from, to, parsedType, parsedReason, page <= 0 ? 1 : page, pageSize);
        return Ok(await _approvals.ListPendingAsync(_user.EmployeeId ?? 0, isManager, isHr, filter, cancellationToken));
    }

    /// <summary>A manager or HR user approves or rejects an approval waiting for a decision.</summary>
    [HttpPost("{id:int}/decision")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<ApprovalDecisionResponse>> Decide(int id, DecideApprovalRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } decidedBy)
        {
            return NoEmployeeIdentity();
        }

        // A user with one of the two roles decides in it; one with both says which.
        var isManager = _user.IsInRole(AttendanceRoles.Manager);
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        var asHr = isManager && isHr ? request.IsHrDecision : isHr;

        var result = await _approvals.DecideAsync(id, decidedBy, asHr, request.Approved, request.Reason, cancellationToken);
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

        return new ApprovalDecisionResponse
        {
            ApprovalId = result.ApprovalId!.Value,
            Type = result.Type!.Value.ToString(),
            Status = result.Status!.Value.ToString(),
            ExceptionStatus = result.ExceptionStatus?.ToString(),
            RecordStatus = result.RecordStatus?.ToString(),
        };
    }

    private ObjectResult NoEmployeeIdentity() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim).");
}
