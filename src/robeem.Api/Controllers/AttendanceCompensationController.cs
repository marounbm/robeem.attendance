using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The compensation report: each employee's overtime, under time and penalties for their own pay period, and the payroll administrator's
/// decision on each line. Overtime and under time are not exceptions; they are decided here. Days come only through the payroll gate.
/// The person who decides is the authenticated user, never a field of the request.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.PayrollAdministrator)]
[Route("api/attendance/payroll/compensation")]
public sealed class AttendanceCompensationController : ControllerBase
{
    private readonly IPayrollCompensationService _compensation;
    private readonly ICurrentUser _user;

    public AttendanceCompensationController(IPayrollCompensationService compensation, ICurrentUser user)
    {
        _compensation = compensation;
        _user = user;
    }

    /// <summary>One page of the employees with the totals of their report. <paramref name="mode"/> is Last (each employee's latest finished period, the default) or Containing (the period that contains <paramref name="date"/>, today when none).</summary>
    [HttpGet]
    public async Task<PagedResult<CompensationOverviewItem>> List(string? search, string? mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken) =>
        await _compensation.ListAsync(search, mode, date, page <= 0 ? 1 : page, pageSize, cancellationToken);

    /// <summary>Every employee of the overview, for an export. Not paged: it is one row per employee, read a page at a time underneath.</summary>
    [HttpGet("all")]
    public async Task<IReadOnlyList<CompensationOverviewItem>> ListAll(string? search, string? mode, DateTime? date, CancellationToken cancellationToken) =>
        await _compensation.ListAllAsync(search, mode, date, cancellationToken);

    /// <summary>One employee's report: the lines, the totals, the balance and the days that are not ready.</summary>
    [HttpGet("{employeeId:int}")]
    public async Task<ActionResult<CompensationReport>> Report(int employeeId, string? mode, DateTime? date, CancellationToken cancellationToken) =>
        await _compensation.GetReportAsync(employeeId, mode, date, cancellationToken) is { } report ? report : NotFound();

    /// <summary>Records decisions on lines of an employee's report, all or none. A leave decision adds leaves to the employee's leaves.</summary>
    [HttpPost("decisions")]
    public async Task<ActionResult<CompensationDecidedResponse>> Decide(CompensationDecisionRequest request, CancellationToken cancellationToken)
    {
        if (_user.EmployeeId is not { } decidedBy)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Your account is not linked to an employee (no employee_id claim).");
        }

        var result = await _compensation.DecideAsync(request, decidedBy, cancellationToken);
        return result.Succeeded ? new CompensationDecidedResponse { Decided = result.Id!.Value } : Failure(result);
    }

    /// <summary>Closes the employee's pay period. Refused while it has not ended or has days that are not ready.</summary>
    [HttpPost("close")]
    public async Task<IActionResult> Close(CompensationCloseRequest request, CancellationToken cancellationToken)
    {
        var result = await _compensation.ClosePeriodAsync(request, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>Opens the employee's closed pay period again, with a reason.</summary>
    [HttpPost("reopen")]
    public async Task<IActionResult> Reopen(CompensationReopenRequest request, CancellationToken cancellationToken)
    {
        var result = await _compensation.ReopenPeriodAsync(request, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    private ActionResult Failure(SaveResult result)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
