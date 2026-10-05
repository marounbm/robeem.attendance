using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using robeem.Api.Security;
using robeem.Application.HR.Employees;
using robeem.Contracts.Security;

namespace robeem.Api.Controllers;

/// <summary>
/// The employees a developer can act as in the Web's "act as" switcher. It exists only while the
/// development stand-in user is on (which the Api refuses outside Development); otherwise it is a 404.
/// It is anonymous on purpose: someone acting as a plain Employee must still be able to switch.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/development/actors")]
public sealed class DevelopmentActorsController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly ApiAuthenticationOptions _options;

    public DevelopmentActorsController(IEmployeeService employees, IOptions<ApiAuthenticationOptions> options)
    {
        _employees = employees;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActorOption>>> List(CancellationToken cancellationToken)
    {
        if (!_options.DevelopmentUser.Enabled)
        {
            return NotFound();
        }

        var employees = await _employees.ListManagerOptionsAsync(null, cancellationToken);
        return Ok(employees.Select(e => new ActorOption { Id = e.Id, Name = e.Name }).ToList());
    }
}
