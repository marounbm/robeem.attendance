using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.HR.Employees;
using robeem.Contracts.HR.Employees;

namespace robeem.Api.Controllers;

[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/schedules")]
public sealed class SchedulesController : ControllerBase
{
    private readonly IEmployeeService _employees;

    public SchedulesController(IEmployeeService employees)
    {
        _employees = employees;
    }

    /// <summary>Time schedules an employee can be assigned to.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ScheduleOption>> List(CancellationToken cancellationToken)
        => await _employees.ListSchedulesAsync(cancellationToken);
}
