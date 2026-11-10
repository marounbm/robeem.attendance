using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Common;
using robeem.Application.HR.Leaves;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;

namespace robeem.Api.Controllers;

/// <summary>Employees' leaves, approved the moment HR saves them. HR only.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/leaves")]
public sealed class LeavesController : ControllerBase
{
    private readonly ILeaveService _leaves;

    public LeavesController(ILeaveService leaves)
    {
        _leaves = leaves;
    }

    /// <param name="employeeId">Only this employee's leaves.</param>
    /// <param name="search">Part of the employee's number or name.</param>
    /// <param name="from">Leaves that end on or after this date.</param>
    /// <param name="to">Leaves that start on or before this date.</param>
    /// <param name="type">Annual, Sick, Unpaid or Other.</param>
    /// <param name="page">The 1-based page (default 1). A page past the end answers the last page.</param>
    /// <param name="pageSize">Leaves per page (default 20, at most 200).</param>
    [HttpGet]
    public async Task<PagedResult<LeaveListItem>> List(int? employeeId, string? search, DateTime? from, DateTime? to, string? type, int page, int pageSize, CancellationToken cancellationToken)
        => await _leaves.ListAsync(new LeaveFilter(employeeId, search, from, to, type, page <= 0 ? 1 : page, pageSize), cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeaveListItem>> Get(int id, CancellationToken cancellationToken)
        => await _leaves.GetAsync(id, cancellationToken) is { } item ? item : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(LeaveInput input, CancellationToken cancellationToken)
    {
        var result = await _leaves.CreateAsync(input, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { id = result.Id!.Value }, new CreatedResponse { Id = result.Id.Value })
            : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, LeaveInput input, CancellationToken cancellationToken)
    {
        var result = await _leaves.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _leaves.DeleteAsync(id, cancellationToken);
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
