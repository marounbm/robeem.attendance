using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Api.Controllers;

/// <summary>
/// Calculated attendance, day by day. An Employee sees their own days, a Manager their direct
/// reports', HR everyone's; only HR can run a day through the processing engine again.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/records")]
public sealed class AttendanceRecordsController : ControllerBase
{
    private readonly IAttendanceRecordService _records;
    private readonly ICurrentUser _user;

    public AttendanceRecordsController(IAttendanceRecordService records, ICurrentUser user)
    {
        _records = records;
        _user = user;
    }

    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="employeeId">Only this employee (within what the user may see).</param>
    /// <param name="status">Only days with this status: Calculated, PendingReview, Approved or Rejected.</param>
    /// <param name="onlyWithExceptions">Only days with an exception that is not closed.</param>
    /// <param name="take">At most this many, newest first (default 100, at most 500).</param>
    [HttpGet]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<IReadOnlyList<RecordDayListItem>>> List(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int take, CancellationToken cancellationToken)
    {
        if (Viewer() is not { } viewer)
        {
            return NotLinked();
        }

        AttendanceRecordStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AttendanceRecordStatus>(status, ignoreCase: true, out var value))
            {
                ModelState.AddModelError(nameof(status), $"The status must be one of: {string.Join(", ", Enum.GetNames<AttendanceRecordStatus>())}.");
                return ValidationProblem(ModelState);
            }

            parsed = value;
        }

        return Ok(await _records.ListDaysAsync(new RecordFilter(from, to, employeeId, parsed, onlyWithExceptions, take == 0 ? 100 : take), viewer, cancellationToken));
    }

    /// <summary>One employee's day in full. 404 when the employee doesn't exist or the user may not see them.</summary>
    [HttpGet("{employeeId:int}/{date:datetime}")]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<RecordDayDetail>> Get(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        if (Viewer() is not { } viewer)
        {
            return NotLinked();
        }

        return await _records.GetDayAsync(employeeId, date, viewer, cancellationToken) is { } detail ? detail : NotFound();
    }

    /// <summary>Runs the processing engine again for the day. A changed result is a new version; nothing is rewritten.</summary>
    [HttpPost("{employeeId:int}/{date:datetime}/reprocess")]
    [Authorize(Roles = AttendanceRoles.HRAdministrator)]
    public async Task<ActionResult<ReprocessResponse>> Reprocess(int employeeId, DateTime date, CancellationToken cancellationToken) =>
        await _records.ReprocessAsync(employeeId, date, cancellationToken) is { } result ? result : NotFound();

    /// <summary>
    /// Who is looking. An Employee's or Manager's scope depends on who they are, so without an
    /// <c>employee_id</c> there is nobody to scope to. HR sees everyone and needs no employee of their
    /// own: an HR user without one is a viewer with employee id 0, which no employee has, and with
    /// only the HR scope.
    /// </summary>
    private ExceptionViewer? Viewer()
    {
        var isHr = _user.IsInRole(AttendanceRoles.HRAdministrator);
        if (_user.EmployeeId is { } viewerId)
        {
            return new ExceptionViewer(viewerId, _user.IsInRole(AttendanceRoles.Employee), _user.IsInRole(AttendanceRoles.Manager), isHr);
        }

        return isHr ? new ExceptionViewer(0, false, false, true) : null;
    }

    private ObjectResult NotLinked() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim). Only HR can see attendance without one.");
}
