using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The exceptions of an employee's shift on a date, grouped as one case to settle. An Employee sees their own, a Manager their direct
/// reports', HR everyone's; a user in several of these roles sees the union. The exceptions themselves are at <c>api/attendance/exceptions</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/exception-groups")]
public sealed class AttendanceExceptionGroupsController : ControllerBase
{
    private readonly IAttendanceExceptionGroupQuery _groups;
    private readonly IAttendanceExceptionGroupService _service;
    private readonly ICurrentUser _user;

    public AttendanceExceptionGroupsController(IAttendanceExceptionGroupQuery groups, IAttendanceExceptionGroupService service, ICurrentUser user)
    {
        _groups = groups;
        _service = service;
        _user = user;
    }

    /// <param name="status">Active (the default: Open and PendingCorrection), Open, PendingCorrection, Closed or All.</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="employeeId">Only this employee (within what the user may see).</param>
    /// <param name="page">The 1-based page; a missing or non-positive page is 1, and one past the end answers the last page.</param>
    /// <param name="pageSize">Groups per page, 1 to 200 (default 20).</param>
    [HttpGet]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<PagedResult<ExceptionGroupListItem>>> List(string? status, DateTime? from, DateTime? to, int? employeeId, int page, int pageSize, CancellationToken cancellationToken)
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

        return Ok(await _groups.ListAsync(new ExceptionGroupFilter(parsed, from, to, employeeId, Math.Max(page, 1), pageSize), viewer, cancellationToken));
    }

    /// <summary>
    /// A manager (of the employee) or HR accepts the whole group as it is, with one reason. Refused when it holds a problem that has to be
    /// corrected, such as a missing punch.
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
