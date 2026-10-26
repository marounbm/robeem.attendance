using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Integrations: the connections to attendance vendors, health, and sync-now (file upload is the Uploads
/// screen; devices and their vendor-id mappings are the Devices screen). An integration is paused, never deleted (raw data points at
/// it). The connection secret is only ever a reference here; the secret lives in configuration.
/// </summary>
public sealed class IntegrationsController : Controller
{
    private readonly IIntegrationsApiClient _integrations;
    private readonly IConnectionsApiClient _connections;

    public IntegrationsController(IIntegrationsApiClient integrations, IConnectionsApiClient connections)
    {
        _integrations = integrations;
        _connections = connections;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new IntegrationsIndexViewModel { Integrations = await _integrations.ListAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var connectors = await _integrations.ListConnectorsAsync(cancellationToken);
        return View(new IntegrationFormViewModel
        {
            Input = new IntegrationInput
            {
                ConnectorId = connectors.FirstOrDefault(p => p.IsActive)?.Id ?? 0,
                Transport = connectors.FirstOrDefault(p => p.IsActive)?.SupportedTransports.FirstOrDefault(t => t.IsActive)?.Key ?? "FileUpload",
            },
            Connectors = connectors,
            PayloadFormats = await _integrations.ListPayloadFormatOptionsAsync(cancellationToken),
            Descriptors = await _connections.ListDescriptorsAsync(cancellationToken),
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
                TempData["StatusMessage"] = $"Integration '{model.Input.DisplayName}' was created. Add its devices next: each device carries its employee-id mappings.";
                return RedirectToAction(nameof(Details), new { id = result.Id });
            }

            AddErrors(result, nameof(IntegrationFormViewModel.Input));
        }

        model.Connectors = await _integrations.ListConnectorsAsync(cancellationToken);
        model.PayloadFormats = await _integrations.ListPayloadFormatOptionsAsync(cancellationToken);
        model.Descriptors = await _connections.ListDescriptorsAsync(cancellationToken);
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
            Connectors = await _integrations.ListConnectorsAsync(cancellationToken),
            PayloadFormats = await _integrations.ListPayloadFormatOptionsAsync(cancellationToken),
            Descriptors = await _connections.ListDescriptorsAsync(cancellationToken),
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
        model.Connectors = await _integrations.ListConnectorsAsync(cancellationToken);
        model.PayloadFormats = await _integrations.ListPayloadFormatOptionsAsync(cancellationToken);
        model.Descriptors = await _connections.ListDescriptorsAsync(cancellationToken);
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
        });
    }

    /// <summary>
    /// Each raw punch of the integration beside the normalized punch it produced, with the vendor's
    /// mapping rules. A page of its own, not part of Details: every open of it is a read of raw
    /// attendance that the Api writes to the audit log.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> RawMapping(int id, int? batchId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = IntegrationRawMappingViewModel.PageSizes.Contains(pageSize) ? pageSize : IntegrationRawMappingViewModel.DefaultPageSize;
        if (await _integrations.GetAsync(id, cancellationToken) is not { } integration
            || await _integrations.GetRawMappingGuideAsync(id, cancellationToken) is not { } guide
            || await _integrations.ListRawMappingAsync(id, batchId, status, page <= 0 ? 1 : page, size, cancellationToken) is not { } rows)
        {
            return NotFound();
        }

        return View(new IntegrationRawMappingViewModel
        {
            Integration = integration,
            Guide = guide,
            Rows = rows.Items,
            BatchId = batchId,
            Status = status,
            Page = rows.Page,
            PageSize = size,
            Total = rows.Total,
        });
    }

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

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
