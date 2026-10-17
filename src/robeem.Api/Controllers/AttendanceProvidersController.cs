using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>
/// The catalog of attendance providers. A provider is made inactive, never deleted (integrations and punches point at it).
/// To choose a provider for an integration, see <c>api/attendance/integrations/providers</c>.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/providers")]
public sealed class AttendanceProvidersController : ControllerBase
{
    private readonly IAttendanceProviderService _providers;

    public AttendanceProvidersController(IAttendanceProviderService providers)
    {
        _providers = providers;
    }

    [HttpGet]
    public async Task<IReadOnlyList<ProviderListItem>> List(CancellationToken cancellationToken) =>
        await _providers.ListAsync(cancellationToken);

    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<ProviderInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _providers.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(ProviderInput input, CancellationToken cancellationToken)
    {
        var result = await _providers.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ProviderInput input, CancellationToken cancellationToken)
    {
        var result = await _providers.UpdateAsync(id, input, cancellationToken);
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
