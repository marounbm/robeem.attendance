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

    public AttendancePayrollController(IPayrollAttendanceExtract payroll)
    {
        _payroll = payroll;
    }

    /// <summary>The approved, exception-free days of the period, both dates inclusive.</summary>
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
}
