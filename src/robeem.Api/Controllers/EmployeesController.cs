using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.HR.Employees;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Api.Controllers;

[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/employees")]
public sealed class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;

    public EmployeesController(IEmployeeService employees)
    {
        _employees = employees;
    }

    /// <param name="search">Part of the number, name, email or department.</param>
    /// <param name="includeInactive">Also the employees who are no longer active.</param>
    /// <param name="page">The 1-based page (default 1). A page past the end answers the last page.</param>
    /// <param name="pageSize">Employees per page (default 20, at most 200).</param>
    [HttpGet]
    public async Task<PagedResult<EmployeeListItem>> List(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken)
        => await _employees.ListAsync(search, includeInactive, page <= 0 ? 1 : page, pageSize, cancellationToken);

    /// <summary>Employees who can be chosen as a manager; <paramref name="excludeId"/> is left out (nobody manages themselves).</summary>
    [HttpGet("manager-options")]
    public async Task<IReadOnlyList<ManagerOption>> ListManagerOptions(int? excludeId, CancellationToken cancellationToken)
        => await _employees.ListManagerOptionsAsync(excludeId, cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeInput>> Get(int id, CancellationToken cancellationToken)
        => await _employees.GetAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(EmployeeInput input, CancellationToken cancellationToken)
    {
        var result = await _employees.CreateAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            return Invalid(result);
        }

        var id = result.EmployeeId!.Value;
        return CreatedAtAction(nameof(Get), new { id }, new CreatedResponse { Id = id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, EmployeeInput input, CancellationToken cancellationToken)
    {
        var result = await _employees.UpdateAsync(id, input, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        return result.Succeeded ? NoContent() : Invalid(result);
    }

    /// <summary>A 400 ValidationProblemDetails whose error keys are <see cref="EmployeeInput"/> property names.</summary>
    private ActionResult Invalid(EmployeeSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
