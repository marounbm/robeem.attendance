using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Common;
using robeem.Application.HR.Holidays;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Holidays;

namespace robeem.Api.Controllers;

/// <summary>The tenant's public holidays, one row per day. HR only.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/holidays")]
public sealed class HolidaysController : ControllerBase
{
    private readonly IHolidayService _holidays;

    public HolidaysController(IHolidayService holidays)
    {
        _holidays = holidays;
    }

    /// <param name="year">The calendar year (default: the current one).</param>
    [HttpGet]
    public async Task<IReadOnlyList<HolidayItem>> List(int? year, CancellationToken cancellationToken)
        => await _holidays.ListAsync(year is > 0 and < 10000 ? year.Value : DateTime.Today.Year, cancellationToken);

    /// <summary>The years that have at least one holiday, latest first.</summary>
    [HttpGet("years")]
    public async Task<IReadOnlyList<int>> Years(CancellationToken cancellationToken)
        => await _holidays.ListYearsAsync(cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HolidayItem>> Get(int id, CancellationToken cancellationToken)
        => await _holidays.GetAsync(id, cancellationToken) is { } item ? item : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(HolidayInput input, CancellationToken cancellationToken)
    {
        var result = await _holidays.CreateAsync(input, cancellationToken);
        return result.Succeeded
            ? CreatedAtAction(nameof(Get), new { id = result.Id!.Value }, new CreatedResponse { Id = result.Id.Value })
            : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, HolidayInput input, CancellationToken cancellationToken)
    {
        var result = await _holidays.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await _holidays.DeleteAsync(id, cancellationToken);
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
