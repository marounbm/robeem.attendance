using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>The kinds of punch device a device can be given. A type is deactivated, never deleted: devices point at it.</summary>
public sealed class DeviceTypesController : Controller
{
    private readonly IDeviceTypesApiClient _types;

    public DeviceTypesController(IDeviceTypesApiClient types)
    {
        _types = types;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new DeviceTypesIndexViewModel { Types = await _types.ListAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Create(DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid ? await _types.CreateAsync(input, cancellationToken) : Invalid();
        return Done(result, $"Device type '{input.Name}' was added.");
    }

    [HttpPost]
    public async Task<IActionResult> Update(int id, DeviceTypeInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid ? await _types.UpdateAsync(id, input, cancellationToken) : Invalid();
        return Done(result, $"Device type '{input.Name}' was saved.");
    }

    private static ApiSaveResult Invalid() => ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "Give the type a name." });

    private IActionResult Done(ApiSaveResult result, string success)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? success : result.MessageText;
        return RedirectToAction(nameof(Index));
    }
}
