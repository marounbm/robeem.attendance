using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>The payroll gate (study §18.3): the only way attendance leaves this feature.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.PayrollAdministrator)]
[Route("api/attendance/payroll")]
public sealed class AttendancePayrollController : ControllerBase
{
    private readonly IPayrollAttendanceExtract _payroll;
    private readonly IPayrollApprovedPageService _pages;

    public AttendancePayrollController(IPayrollAttendanceExtract payroll, IPayrollApprovedPageService pages)
    {
        _payroll = payroll;
        _pages = pages;
    }

    /// <summary>The approved, exception-free days of the period, both dates inclusive: all of them, for an export.</summary>
    [HttpGet("approved")]
    public async Task<ActionResult<IReadOnlyList<PayrollAttendanceDay>>> GetApproved(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (to.Date < from.Date)
        {
            ModelState.AddModelError(nameof(to), "The period ends before it starts.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _payroll.GetApprovedRecordsAsync(from, to, cancellationToken));
    }

    /// <summary>
    /// The same days, one page of them for a screen (by employee number, then date), with the totals per employee
    /// over all of the days. <paramref name="pageSize"/> is 1 to 200 (default 20).
    /// </summary>
    [HttpGet("approved/page")]
    public async Task<ActionResult<PayrollApprovedPage>> GetApprovedPage(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (to.Date < from.Date)
        {
            ModelState.AddModelError(nameof(to), "The period ends before it starts.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _pages.GetPageAsync(from, to, page <= 0 ? 1 : page, pageSize, cancellationToken));
    }
}
