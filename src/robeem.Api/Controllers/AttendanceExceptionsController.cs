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
    private readonly ICurrentUser _user;

    public AttendanceExceptionsController(IAttendanceExceptionQuery exceptions, ICurrentUser user)
    {
        _exceptions = exceptions;
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
        if (_user.EmployeeId is not { } viewerId)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim).");
        }

        var parsed = ExceptionStatusFilter.Active;
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: true, out parsed))
        {
            ModelState.AddModelError(nameof(status), $"The status must be one of: {string.Join(", ", Enum.GetNames<ExceptionStatusFilter>())}.");
            return ValidationProblem(ModelState);
        }

        var viewer = new ExceptionViewer(
            viewerId,
            _user.IsInRole(AttendanceRoles.Employee),
            _user.IsInRole(AttendanceRoles.Manager),
            _user.IsInRole(AttendanceRoles.HRAdministrator));
        return Ok(await _exceptions.ListAsync(new ExceptionFilter(parsed, from, to, employeeId, take == 0 ? 100 : take), viewer, cancellationToken));
    }
}
