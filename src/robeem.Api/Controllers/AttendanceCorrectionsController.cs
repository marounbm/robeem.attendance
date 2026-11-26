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
    private readonly IAttendanceDayCorrectionService _days;
    private readonly ICurrentUser _user;

    public AttendanceCorrectionsController(IAttendanceCorrectionService corrections, IAttendanceDayCorrectionService days, ICurrentUser user)
    {
        _corrections = corrections;
        _days = days;
        _user = user;
    }

    /// <summary>
    /// One day of an employee with every punch and what can be done to it, for correcting it: the user's own, or, for their manager or HR,
    /// another employee's (<paramref name="employeeId"/>). Reading the punches is audited, with who read them.
    /// </summary>
    /// <param name="date">The attendance date.</param>
    /// <param name="employeeId">The employee whose day it is; omitted for the user's own. Anyone but that employee's manager or HR gets a 404.</param>
    [HttpGet("day")]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<DayCorrectionView>> Day(DateTime date, int? employeeId, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } self)
        {
            return NoEmployeeIdentity();
        }

        return await _days.GetDayAsync(employeeId ?? self, date, Viewer(self), cancellationToken) is { } view ? view : NotFound();
    }

    /// <summary>
    /// Sends the corrections of one day: punches given another type, missing punches added, punches ignored. They are decided together.
    /// The sender is the signed-in user, never a field of the request. The day is theirs, unless the request names another employee:
    /// only that employee's manager or HR may, and they take the steps of the approval they hold (the correction may apply at once).
    /// </summary>
    [HttpPost("day")]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<CreatedResponse>> ProposeDay(DayCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } self)
        {
            return NoEmployeeIdentity();
        }

        var result = await _days.ProposeAsync(request.EmployeeId ?? self, request, Viewer(self), cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            return StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value });
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }

    /// <summary>An employee proposes the missing punch of their own open exception, or the type an out-of-order punch of theirs should have.</summary>
    [HttpPost]
    [Authorize(Roles = AttendanceRoles.Employee)]
    public async Task<ActionResult<CreatedResponse>> Propose(ProposeCorrectionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } requestedBy)
        {
            return NoEmployeeIdentity();
        }

        // A type is proposed for an out-of-order punch, a time for a missing one.
        var result = string.IsNullOrWhiteSpace(request.NewEventType)
            ? await _corrections.ProposeCorrectionAsync(request.ExceptionId, request.ProposedValue, request.Justification, requestedBy, cancellationToken)
            : await _corrections.ProposeTypeChangeAsync(request.ExceptionId, request.NewEventType, request.Justification, requestedBy, cancellationToken);
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

        // The Manager queue is the user's direct reports', so it needs to know who they are. The HR queue
        // is nobody's own, so HR can look at it with no employee of their own (id 0 matches no employee).
        if (_user.EmployeeId is null && awaiting == CorrectionApprover.Manager)
        {
            return NoEmployeeIdentity();
        }

        return Ok(await _corrections.ListPendingAsync(awaiting, _user.EmployeeId ?? 0, cancellationToken));
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

    /// <summary>The signed-in user and the roles they hold, which decide whose days they may correct.</summary>
    private ExceptionViewer Viewer(int employeeId) =>
        new(employeeId, _user.IsInRole(AttendanceRoles.Employee), _user.IsInRole(AttendanceRoles.Manager), _user.IsInRole(AttendanceRoles.HRAdministrator));

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
