using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>How often each employee is paid: the tenant's default pay cycle and an employee's own. Every change is audited.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.PayrollAdministrator)]
[Route("api/attendance/pay-cycles")]
public sealed class AttendancePayCyclesController : ControllerBase
{
    private readonly IPayCycleService _cycles;

    public AttendancePayCyclesController(IPayCycleService cycles)
    {
        _cycles = cycles;
    }

    /// <summary>The default cycle as it applies, with the periods around today (the calendar month when none is set).</summary>
    [HttpGet("default")]
    public async Task<PayCycleItem> GetDefault(CancellationToken cancellationToken) =>
        await _cycles.DescribeDefaultAsync(cancellationToken);

    [HttpPut("default")]
    public async Task<IActionResult> SaveDefault(PayCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _cycles.SaveDefaultAsync(input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>One page of the active employees with the cycle that applies to each.</summary>
    [HttpGet]
    public async Task<PagedResult<EmployeePayCycleItem>> List(string? search, int page, int pageSize, CancellationToken cancellationToken) =>
        await _cycles.ListEmployeesAsync(search, page <= 0 ? 1 : page, pageSize, cancellationToken);

    /// <summary>An employee's own cycle; 404 when they have none.</summary>
    [HttpGet("employees/{employeeId:int}")]
    public async Task<ActionResult<PayCycleInput>> GetEmployee(int employeeId, CancellationToken cancellationToken) =>
        await _cycles.GetEmployeeAsync(employeeId, cancellationToken) is { } cycle ? cycle : NotFound();

    [HttpPut("employees/{employeeId:int}")]
    public async Task<IActionResult> SaveEmployee(int employeeId, PayCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _cycles.SaveEmployeeAsync(employeeId, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>Takes away an employee's own cycle, so the default applies to them again.</summary>
    [HttpDelete("employees/{employeeId:int}")]
    public async Task<IActionResult> RemoveEmployee(int employeeId, CancellationToken cancellationToken)
    {
        var result = await _cycles.RemoveEmployeeAsync(employeeId, cancellationToken);
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
