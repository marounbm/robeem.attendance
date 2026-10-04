using Microsoft.AspNetCore.Mvc;
using robeem.Application.HR.Employees;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Api.Controllers;

[ApiController]
[Route("api/employees")]
public sealed class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;

    public EmployeesController(IEmployeeService employees)
    {
        _employees = employees;
    }

    [HttpGet]
    public async Task<IReadOnlyList<EmployeeListItem>> List(string? search, bool includeInactive, CancellationToken cancellationToken)
        => await _employees.ListAsync(search, includeInactive, cancellationToken);

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
