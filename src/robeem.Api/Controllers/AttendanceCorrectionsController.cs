using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// Corrections of attendance exceptions (study §11). Who proposes and who decides is the
/// authenticated user (their <c>employee_id</c> claim), and the role they decide in is checked
/// against their roles.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/corrections")]
public sealed class AttendanceCorrectionsController : ControllerBase
{
    private readonly IAttendanceCorrectionService _corrections;
    private readonly ICurrentUser _user;

    public AttendanceCorrectionsController(IAttendanceCorrectionService corrections, ICurrentUser user)
    {
        _corrections = corrections;
        _user = user;
    }

    /// <summary>An employee proposes the missing punch of their own open exception.</summary>
    [HttpPost]
    [Authorize(Roles = AttendanceRoles.Employee)]
    public async Task<ActionResult<CreatedResponse>> Propose(ProposeCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } requestedBy)
        {
            return NoEmployeeIdentity();
        }

        var result = await _corrections.ProposeCorrectionAsync(request.ExceptionId, request.ProposedValue, request.Justification, requestedBy, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.CorrectionId!.Value })
            : Invalid(result);
    }

    /// <summary>Lists the pending corrections waiting for a manager's decision, or for HR's: the queue of the role the user holds.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<IReadOnlyList<CorrectionListItem>>> ListPending(CorrectionApprover awaiting, CancellationToken cancellationToken)
    {
        var role = awaiting == CorrectionApprover.Manager ? AttendanceRoles.Manager : AttendanceRoles.HRAdministrator;
        if (!_user.IsInRole(role))
        {
            return Forbid();
        }

        if (_user.EmployeeId is not { } approverId)
        {
            return NoEmployeeIdentity();
        }

        return Ok(await _corrections.ListPendingAsync(awaiting, approverId, cancellationToken));
    }

    /// <summary>A manager or HR user approves or rejects a pending correction.</summary>
    [HttpPost("{id:int}/decision")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<ActionResult<CorrectionDecisionResponse>> Decide(int id, DecideCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } decidedBy)
        {
            return NoEmployeeIdentity();
        }

        // A user with one of the two roles decides in it; one with both says which.
        var isManager = _user.IsInRole(AttendanceRoles.Manager);
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        var asHr = isManager && isHr ? request.IsHrDecision : isHr;

        var result = await _corrections.DecideCorrectionAsync(id, decidedBy, request.Approved, asHr, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Succeeded
            ? new CorrectionDecisionResponse
            {
                CorrectionId = id,
                Status = result.Status!.Value.ToString(),
                ExceptionStatus = result.ExceptionStatus!.Value.ToString(),
            }
            : Invalid(result);
    }

    private ObjectResult NoEmployeeIdentity() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim).");

    /// <summary>A 400 ValidationProblemDetails whose error keys are request property names.</summary>
    private ActionResult Invalid(CorrectionResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
