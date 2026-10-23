using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// Attendance exceptions. An Employee sees their own, a Manager their direct reports', HR everyone's;
/// a user in several of these roles sees the union.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/exceptions")]
public sealed class AttendanceExceptionsController : ControllerBase
{
    private readonly IAttendanceExceptionQuery _exceptions;
    private readonly IAttendanceExceptionService _service;
    private readonly ICurrentUser _user;

    public AttendanceExceptionsController(IAttendanceExceptionQuery exceptions, IAttendanceExceptionService service, ICurrentUser user)
    {
        _exceptions = exceptions;
        _service = service;
        _user = user;
    }

    /// <param name="status">Active (the default: Open and PendingCorrection), Open, PendingCorrection, Closed or All.</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="employeeId">Only this employee (within what the user may see).</param>
    /// <param name="take">At most this many, newest first (default 100, at most 500).</param>
    [HttpGet]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<IReadOnlyList<ExceptionListItem>>> List(string? status, DateTime? from, DateTime? to, int? employeeId, int take, CancellationToken cancellationToken)
    {
        if (AttendanceViewers.For(_user) is not { } viewer)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim). Only HR can see exceptions without one.");
        }

        var parsed = ExceptionStatusFilter.Active;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: true, out parsed))
        {
            ModelState.AddModelError(nameof(status), $"The status must be one of: {string.Join(", ", Enum.GetNames<ExceptionStatusFilter>())}.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _exceptions.ListAsync(new ExceptionFilter(parsed, from, to, employeeId, take == 0 ? 100 : take), viewer, cancellationToken));
    }

    /// <summary>
    /// A manager (of the employee) or HR accepts a break exception (long, late or early) as it is, with a reason. A missing punch
    /// is not accepted: it is corrected.
    /// </summary>
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = AttendanceRoles.Approvers)]
    public async Task<IActionResult> Accept(int id, AcceptExceptionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } acceptedBy)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim), so it can't accept an exception.");
        }

        // A user with one of the two roles accepts in it; one with both says which.
        var isManager = _user.IsInRole(AttendanceRoles.Manager);
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        var asHr = isManager && isHr ? request.IsHrDecision : isHr;

        var result = await _service.AcceptAsync(id, acceptedBy, asHr, request.Reason, cancellationToken);
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

        return NoContent();
    }
}
