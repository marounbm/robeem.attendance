using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Integrations: the connections to attendance providers, their devices and vendor-id mappings,
/// health, and sync-now (file upload is the Uploads screen). An integration is paused, never deleted (raw data points at
/// it). The connection secret is only ever a reference here; the secret lives in configuration.
/// </summary>
public sealed class IntegrationsController : Controller
{
    private readonly IIntegrationsApiClient _integrations;

    public IntegrationsController(IIntegrationsApiClient integrations)
    {
        _integrations = integrations;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new IntegrationsIndexViewModel { Integrations = await _integrations.ListAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var providers = await _integrations.ListProvidersAsync(cancellationToken);
        return View(new IntegrationFormViewModel
        {
            Input = new IntegrationInput
            {
                ProviderId = providers.FirstOrDefault(p => p.IsActive)?.Id ?? 0,
                Transport = providers.FirstOrDefault(p => p.IsActive)?.SupportedTransports.FirstOrDefault(t => t.IsActive)?.Key ?? "FileUpload",
            },
            Providers = providers,
            FileFormats = await _integrations.ListFileFormatOptionsAsync(cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(IntegrationFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _integrations.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Integration '{model.Input.DisplayName}' was created. Add its devices and employee-id mappings next.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }

            AddErrors(result, nameof(IntegrationFormViewModel.Input));
        }

        model.Providers = await _integrations.ListProvidersAsync(cancellationToken);
        model.FileFormats = await _integrations.ListFileFormatOptionsAsync(cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _integrations.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(new IntegrationFormViewModel
        {
            Id = id,
            Input = input,
            Providers = await _integrations.ListProvidersAsync(cancellationToken),
            FileFormats = await _integrations.ListFileFormatOptionsAsync(cancellationToken),
            Devices = await _integrations.ListDevicesAsync(id, cancellationToken),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, IntegrationFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _integrations.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Integration '{model.Input.DisplayName}' was saved.";
                return RedirectToAction(nameof(Details), new { id });
            }

            AddErrors(result, nameof(IntegrationFormViewModel.Input));
        }

        model.Id = id;
        model.Providers = await _integrations.ListProvidersAsync(cancellationToken);
        model.FileFormats = await _integrations.ListFileFormatOptionsAsync(cancellationToken);
        model.Devices = await _integrations.ListDevicesAsync(id, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (await _integrations.GetAsync(id, cancellationToken) is not { } integration)
        {
            return NotFound();
        }

        return View(new IntegrationDetailsViewModel
        {
            Integration = integration,
            Health = await _integrations.GetHealthAsync(id, cancellationToken),
            Devices = await _integrations.ListDevicesAsync(id, cancellationToken),
            DeviceTypes = await _integrations.ListDeviceTypeOptionsAsync(cancellationToken),
            Mappings = await _integrations.ListMappingsAsync(id, cancellationToken),
            Employees = await _integrations.ListEmployeeOptionsAsync(cancellationToken),
        });
    }

    /// <summary>
    /// Each raw punch of the integration beside the normalized punch it produced, with the provider's
    /// mapping rules. A page of its own, not part of Details: every open of it is a read of raw
    /// attendance that the Api writes to the audit log.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> RawMapping(int id, int? batchId, string? status, int take, CancellationToken cancellationToken)
    {
        take = take <= 0 ? 100 : Math.Min(take, 500);
        if (await _integrations.GetAsync(id, cancellationToken) is not { } integration
            || await _integrations.GetRawMappingGuideAsync(id, cancellationToken) is not { } guide
            || await _integrations.ListRawMappingAsync(id, batchId, status, take, cancellationToken) is not { } rows)
        {
            return NotFound();
        }

        return View(new IntegrationRawMappingViewModel { Integration = integration, Guide = guide, Rows = rows, BatchId = batchId, Status = status, Take = take });
    }

    [HttpPost]
    public async Task<IActionResult> AddDevice(int id, DeviceInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid ? await _integrations.AddDeviceAsync(id, input, cancellationToken) : Invalid();
        return Done(id, result, $"Device '{input.Name}' was added.");
    }

    [HttpPost]
    public async Task<IActionResult> UpdateDevice(int id, int deviceId, DeviceInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid ? await _integrations.UpdateDeviceAsync(id, deviceId, input, cancellationToken) : Invalid();
        return Done(id, result, $"Device '{input.Name}' was saved.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDevice(int id, int deviceId, CancellationToken cancellationToken) =>
        Done(id, await _integrations.DeleteDeviceAsync(id, deviceId, cancellationToken), "The device was deleted.");

    [HttpPost]
    public async Task<IActionResult> AddMapping(int id, MappingInput input, CancellationToken cancellationToken)
    {
        var result = ModelState.IsValid ? await _integrations.AddMappingAsync(id, input, cancellationToken) : Invalid();
        return Done(id, result, $"Vendor id '{input.ExternalEmployeeId}' was mapped.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMapping(int id, int mappingId, CancellationToken cancellationToken) =>
        Done(id, await _integrations.DeleteMappingAsync(id, mappingId, cancellationToken), "The mapping was removed.");

    [HttpPost]
    public async Task<IActionResult> Run(int id, bool reconcile, CancellationToken cancellationToken)
    {
        var outcome = await _integrations.RunAsync(id, reconcile, cancellationToken);
        var what = reconcile ? "Reconciliation" : "Sync";
        var text = $"{what}: {outcome.Status}, {outcome.RecordsIngested} record(s).{(string.IsNullOrWhiteSpace(outcome.Message) ? string.Empty : " " + outcome.Message)}";

        // Succeeded and Skipped are information; a failure or an open breaker is something to act on.
        TempData[outcome.Status is "Failed" or "CircuitOpen" ? "ErrorMessage" : "StatusMessage"] = text;
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RetryFailed(int id, CancellationToken cancellationToken)
    {
        if (await _integrations.RetryFailedAsync(id, cancellationToken) is not { } result)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = result.Requeued == 0
            ? "No punch is waiting on a time zone."
            : $"{result.Requeued} punch(es) were queued to be normalized again; that takes a few seconds.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RenormalizeFlagged(int id, CancellationToken cancellationToken)
    {
        if (await _integrations.RenormalizeFlaggedAsync(id, cancellationToken) is not { } result)
        {
            return NotFound();
        }

        if (result.Requeued > 0)
        {
            TempData["StatusMessage"] = $"{result.Requeued} punch(es) were queued to be normalized again; that takes a few seconds, and their days are then calculated."
                + (result.Unresolved > 0 ? $" {result.Unresolved} other flagged punch(es) can't be resolved yet." : string.Empty);
        }
        else if (result.Unresolved == 0)
        {
            TempData["StatusMessage"] = "No punch of this integration is flagged for an unrecognized device right now. If you pressed this button before, they were already queued: look at Normalized transactions.";
        }
        else
        {
            TempData["ErrorMessage"] = $"{result.Unresolved} flagged punch(es) can't be resolved yet: "
                + (result.Transport == "FileUpload"
                    ? "this integration has no device for uploaded files and their devices are not registered. Edit the integration, choose one under Device for uploaded files, and save."
                    : $"this integration receives attendance by {result.TransportName}, not manual file upload, so it has no device for uploaded files. Register the punches' devices instead.");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private static ApiSaveResult Invalid() => ApiSaveResult.Invalid(new Dictionary<string, string> { [string.Empty] = "Fill in every required field." });

    private IActionResult Done(int id, ApiSaveResult result, string success)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? success : result.MessageText;
        return RedirectToAction(nameof(Details), new { id });
    }

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
