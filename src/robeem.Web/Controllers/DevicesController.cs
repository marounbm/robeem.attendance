using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The punch devices of every integration in one grid, and the page of one device: its details and the
/// mapping of the vendor's employee ids to employees, which is kept per device. A device with punches on record
/// can't be deleted.
/// </summary>
public sealed class DevicesController : Controller
{
    private readonly IIntegrationsApiClient _integrations;
    private readonly IConnectionsApiClient _connections;

    public DevicesController(IIntegrationsApiClient integrations, IConnectionsApiClient connections)
    {
        _integrations = integrations;
        _connections = connections;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, CancellationToken cancellationToken)
    {
        var devices = await _integrations.ListAllDevicesAsync(cancellationToken);
        return View(new DevicesIndexViewModel
        {
            Devices = integrationId is { } only ? devices.Where(d => d.IntegrationId == only).ToList() : devices,
            Integrations = await _integrations.ListAsync(cancellationToken),
            IntegrationId = integrationId,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? integrationId, CancellationToken cancellationToken)
    {
        var integrations = await _integrations.ListAsync(cancellationToken);
        var chosen = integrations.FirstOrDefault(i => i.Id == integrationId) ?? integrations.FirstOrDefault();
        return View(new DeviceFormViewModel
        {
            IntegrationId = chosen?.Id ?? 0,
            Input = new DeviceInput { TimeZoneId = chosen?.TimeZoneId ?? "UTC" },
            Integrations = integrations,
            Connections = await ListConnectionsAsync(integrations.Select(i => i.Id), cancellationToken),
            DeviceTypes = await _integrations.ListDeviceTypeOptionsAsync(cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int integrationId, DeviceFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _integrations.AddDeviceAsync(integrationId, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Device '{model.Input.Name}' was added. Map its employee ids below.";
                return RedirectToAction(nameof(Edit), new { integrationId, id = result.Id });
            }

            AddErrors(result, nameof(DeviceFormViewModel.Input));
        }

        model.IntegrationId = integrationId;
        model.Integrations = await _integrations.ListAsync(cancellationToken);
        model.Connections = await ListConnectionsAsync(model.Integrations.Select(i => i.Id), cancellationToken);
        model.DeviceTypes = await _integrations.ListDeviceTypeOptionsAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int integrationId, int id, CancellationToken cancellationToken)
    {
        if (await _integrations.GetDeviceAsync(integrationId, id, cancellationToken) is not { } device)
        {
            return NotFound();
        }

        var model = new DeviceFormViewModel
        {
            Input = new DeviceInput
            {
                ExternalDeviceId = device.ExternalDeviceId,
                Name = device.Name,
                DeviceTypeId = device.DeviceTypeId ?? 0,
                ConnectionId = device.ConnectionId,
                Model = device.Model,
                SoftwareVersion = device.SoftwareVersion,
                SdkVersion = device.SdkVersion,
                TimeZoneId = device.TimeZoneId,
            },
        };
        await FillAsync(model, device, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int integrationId, int id, DeviceFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _integrations.UpdateDeviceAsync(integrationId, id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Device '{model.Input.Name}' was saved.";
                return RedirectToAction(nameof(Index), new { integrationId });
            }

            AddErrors(result, nameof(DeviceFormViewModel.Input));
        }

        if (await _integrations.GetDeviceAsync(integrationId, id, cancellationToken) is not { } device)
        {
            return NotFound();
        }

        await FillAsync(model, device, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int integrationId, int id, CancellationToken cancellationToken)
    {
        var result = await _integrations.DeleteDeviceAsync(integrationId, id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "The device was deleted.";
            return RedirectToAction(nameof(Index), new { integrationId });
        }

        TempData["ErrorMessage"] = result.MessageText;
        return RedirectToAction(nameof(Edit), new { integrationId, id });
    }

    [HttpPost]
    public async Task<IActionResult> AddMapping(int integrationId, int id, MappingInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid
            ? await _integrations.AddMappingAsync(integrationId, id, input, cancellationToken)
            : ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "Fill in every required field." });
        return MappingDone(integrationId, id, result, $"Vendor id '{input.ExternalEmployeeId}' was mapped.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMapping(int integrationId, int id, int mappingId, CancellationToken cancellationToken) =>
        MappingDone(integrationId, id, await _integrations.DeleteMappingAsync(integrationId, id, mappingId, cancellationToken), "The mapping was removed.");

    private async Task FillAsync(DeviceFormViewModel model, DeviceItem device, CancellationToken cancellationToken)
    {
        model.Id = device.Id;
        model.IntegrationId = device.IntegrationId;
        model.Device = device;
        model.Connections = await ListConnectionsAsync([device.IntegrationId], cancellationToken);
        model.DeviceTypes = await _integrations.ListDeviceTypeOptionsAsync(cancellationToken);
        model.Mappings = await _integrations.ListMappingsAsync(device.IntegrationId, device.Id, cancellationToken);
        model.Employees = await _integrations.ListEmployeeOptionsAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ConnectionListItem>> ListConnectionsAsync(IEnumerable<int> integrationIds, CancellationToken cancellationToken)
    {
        var all = new List<ConnectionListItem>();
        foreach (var integrationId in integrationIds)
        {
            all.AddRange(await _connections.ListAsync(integrationId, cancellationToken) ?? []);
        }

        return all;
    }

    private IActionResult MappingDone(int integrationId, int id, ApiSaveResult result, string success)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? success : result.MessageText;
        return RedirectToAction(nameof(Edit), new { integrationId, id });
    }

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
