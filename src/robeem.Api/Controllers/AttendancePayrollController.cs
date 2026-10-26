using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// The payroll gate (study §18.3): the only way attendance leaves this feature. A request names an employee, a period (both
/// dates) or both; one that names neither is refused with 400, because payroll never reads everything at once.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.PayrollAdministrator)]
[Route("api/attendance/payroll")]
public sealed class AttendancePayrollController : ControllerBase
{
    private readonly IPayrollApprovedPageService _pages;

    public AttendancePayrollController(IPayrollApprovedPageService pages)
    {
        _pages = pages;
    }

    /// <summary>
    /// The approved, exception-free days of the period, both dates inclusive: all of them, for an export.
    /// <paramref name="employee"/> keeps only the employees whose number or name contains it; with no dates,
    /// every date of those employees is read.
    /// </summary>
    [HttpGet("approved")]
    public async Task<ActionResult<IReadOnlyList<PayrollAttendanceDay>>> GetApproved(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken)
    {
        if (PayrollApprovedPageService.Validate(from, to, employee) is { } refusal)
        {
            ModelState.AddModelError(nameof(employee), refusal);
            return ValidationProblem(ModelState);
        }

        return Ok(await _pages.GetDaysAsync(from, to, employee, cancellationToken));
    }

    /// <summary>
    /// The same days, one page of them for a screen (by employee number, then date), with the totals per employee
    /// over all of the days that match <paramref name="employee"/> (a number or name). <paramref name="pageSize"/> is 1 to 200 (default 20).
    /// </summary>
    [HttpGet("approved/page")]
    public async Task<ActionResult<PayrollApprovedPage>> GetApprovedPage(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (PayrollApprovedPageService.Validate(from, to, employee) is { } refusal)
        {
            ModelState.AddModelError(nameof(employee), refusal);
            return ValidationProblem(ModelState);
        }

        return Ok(await _pages.GetPageAsync(from, to, employee, page <= 0 ? 1 : page, pageSize, cancellationToken));
    }
}
