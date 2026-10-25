using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The catalog of attendance vendors, the companies. A vendor is made inactive, never deleted (connectors and punches point at it).
/// How a vendor's data arrives is its connectors' business (<c>api/attendance/connectors</c>).
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/vendors")]
public sealed class AttendanceVendorsController : ControllerBase
{
    private readonly IAttendanceVendorService _vendors;

    public AttendanceVendorsController(IAttendanceVendorService vendors)
    {
        _vendors = vendors;
    }

    [HttpGet]
    public async Task<IReadOnlyList<VendorListItem>> List(CancellationToken cancellationToken) =>
        await _vendors.ListAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<VendorInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _vendors.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(VendorInput input, CancellationToken cancellationToken)
    {
        var result = await _vendors.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, VendorInput input, CancellationToken cancellationToken)
    {
        var result = await _vendors.UpdateAsync(id, input, cancellationToken);
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
