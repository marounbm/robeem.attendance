using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// Pay periods. Closing one ends the payroll extraction of its days; reopening needs a reason.
/// Every change is audited.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.PayrollAdministrator)]
[Route("api/attendance/pay-periods")]
public sealed class AttendancePayPeriodsController : ControllerBase
{
    private readonly IPayPeriodService _periods;

    public AttendancePayPeriodsController(IPayPeriodService periods)
    {
        _periods = periods;
    }

    [HttpGet]
    public async Task<IReadOnlyList<PayPeriodItem>> List(CancellationToken cancellationToken) =>
        await _periods.ListAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(PayPeriodInput input, CancellationToken cancellationToken)
    {
        var result = await _periods.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken)
    {
        var result = await _periods.CloseAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, ReopenPayPeriodRequest request, CancellationToken cancellationToken)
    {
        var result = await _periods.ReopenAsync(id, request.Reason, cancellationToken);
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
