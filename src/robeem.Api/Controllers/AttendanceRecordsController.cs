using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
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
    private const int DefaultPageSize = 20;

    private readonly IAttendanceRecordService _records;
    private readonly IAttendanceLookbackService _lookback;
    private readonly IOptions<AttendanceLookbackOptions> _lookbackOptions;
    private readonly ICurrentUser _user;

    public AttendanceRecordsController(IAttendanceRecordService records, IAttendanceLookbackService lookback, IOptions<AttendanceLookbackOptions> lookbackOptions, ICurrentUser user)
    {
        _records = records;
        _lookback = lookback;
        _lookbackOptions = lookbackOptions;
        _user = user;
    }

    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="employeeId">Only this employee (within what the user may see).</param>
    /// <param name="status">Only days with this status: Calculated, PendingReview, Approved or Rejected.</param>
    /// <param name="onlyWithExceptions">Only days with an exception that is not closed.</param>
    /// <param name="page">The 1-based page (default 1). A page past the end answers the last page.</param>
    /// <param name="pageSize">Days per page (default 20, at most 200).</param>
    /// <param name="sort">The column to sort by: Date (default), Employee, Worked, Overtime, Break, Exceptions or Status.</param>
    /// <param name="descending">Sort from the highest to the lowest (for a date, newest first). The default is oldest first.</param>
    [HttpGet]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<PagedResult<RecordDayListItem>>> List(DateTime? from, DateTime? to, int? employeeId, string? status, bool onlyWithExceptions, int page, int pageSize, string? sort, bool descending, CancellationToken cancellationToken)
    {
        if (AttendanceViewers.For(_user) is not { } viewer)
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

        var field = RecordSortField.Date;
        if (!string.IsNullOrWhiteSpace(sort) && (!Enum.TryParse(sort, ignoreCase: true, out field) || !Enum.IsDefined(field)))
        {
            ModelState.AddModelError(nameof(sort), $"The sort must be one of: {string.Join(", ", Enum.GetNames<RecordSortField>())}.");
            return ValidationProblem(ModelState);
        }

        var filter = new RecordFilter(from, to, employeeId, parsed, onlyWithExceptions, page <= 0 ? 1 : page, pageSize <= 0 ? DefaultPageSize : pageSize, field, descending);
        return Ok(await _records.ListDaysAsync(filter, viewer, cancellationToken));
    }

    /// <summary>The employees whose days the user may see, to filter the list by one of them.</summary>
    [HttpGet("employee-options")]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<IReadOnlyList<EmployeeOption>>> EmployeeOptions(CancellationToken cancellationToken)
    {
        if (AttendanceViewers.For(_user) is not { } viewer)
        {
            return NotLinked();
        }

        return Ok(await _records.ListEmployeeOptionsAsync(viewer, cancellationToken));
    }

    /// <summary>One employee's day in full. 404 when the employee doesn't exist or the user may not see them.</summary>
    [HttpGet("{employeeId:int}/{date:datetime}")]
    [Authorize(Roles = AttendanceRoles.ExceptionViewers)]
    public async Task<ActionResult<RecordDayDetail>> Get(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        if (AttendanceViewers.For(_user) is not { } viewer)
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
    /// Looks for missing days in a period (of one employee, or of everyone) the way the nightly run does for the last days, so that a no-show is
    /// raised for every scheduled day nobody punched on. The nightly run reaches only the last days; this is for a period imported later. At most
    /// a year at a time, ending yesterday; the days beyond one run's limit are reported as deferred.
    /// </summary>
    [HttpPost("check-missing")]
    [Authorize(Roles = AttendanceRoles.HRAdministrator)]
    public async Task<ActionResult<CheckMissingDaysResponse>> CheckMissing(CheckMissingDaysRequest request, CancellationToken cancellationToken)
    {
        if (request.To.Date < request.From.Date)
        {
            ModelState.AddModelError(nameof(CheckMissingDaysRequest.To), "The last day can't be before the first.");
        }
        else if ((request.To.Date - request.From.Date).Days + 1 > CheckMissingDaysRequest.MaxDays)
        {
            ModelState.AddModelError(nameof(CheckMissingDaysRequest.To), $"Look at at most {CheckMissingDaysRequest.MaxDays} days at a time.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await _lookback.CheckPeriodAsync(request.EmployeeId, request.From, request.To, _lookbackOptions.Value.EffectiveMaxDaysPerRun, cancellationToken);
        return new CheckMissingDaysResponse
        {
            From = result.From,
            To = result.To,
            DaysCalculated = result.DaysCalculated,
            DaysFailed = result.DaysFailed,
            DaysDeferred = result.DaysDeferred,
            DatesWithoutData = result.DatesWithoutData.ToList(),
        };
    }

    private ObjectResult NotLinked() =>
        Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim). Only HR can see attendance without one.");
}
