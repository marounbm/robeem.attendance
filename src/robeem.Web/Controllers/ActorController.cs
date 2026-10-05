using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Security;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The Development "act as" switcher: pick an employee and the roles to hold, to try approvals
/// and the role-specific screens as different people. Development only (a 404 elsewhere), and the
/// Api honours the choice only through its development stand-in user. Real sign-in replaces it.
/// </summary>
public sealed class ActorController : Controller
{
    private readonly IActorsApiClient _actors;
    private readonly IWebHostEnvironment _environment;

    public ActorController(IActorsApiClient actors, IWebHostEnvironment environment)
    {
        _actors = actors;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? returnUrl, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() || await _actors.ListAsync(cancellationToken) is not { } actors)
        {
            return NotFound();
        }

        return View(new ActorPickerViewModel { Actors = actors, Current = ActorCookie.Read(Request), ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Select(int? employeeId, string[]? roles, string? preset, string? returnUrl, CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() || await _actors.ListAsync(cancellationToken) is not { } actors)
        {
            return NotFound();
        }

        // A preset button wins over the ticked boxes.
        var chosen = preset is not null && ActorPickerViewModel.Presets.TryGetValue(preset, out var presetRoles) ? presetRoles : roles ?? [];
        if (chosen.Any(r => !ActorProtocol.Roles.Contains(r)))
        {
            return BadRequest("Unknown role.");
        }

        string name;
        if (employeeId is { } id)
        {
            if (actors.FirstOrDefault(a => a.Id == id) is not { } actor)
            {
                return BadRequest("Unknown employee.");
            }

            name = actor.Name;
        }
        else
        {
            name = "No employee";
        }

        ActorCookie.Write(Response, Request.IsHttps, employeeId, name, chosen.Distinct());
        return Back(returnUrl);
    }

    [HttpPost]
    public IActionResult Clear(string? returnUrl)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        ActorCookie.Clear(Response);
        return Back(returnUrl);
    }

    private IActionResult Back(string? returnUrl) => LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
}
