using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The catalog of connectors: the software and protocols each vendor offers, the transports each supports, the payload format
/// its uploads are read by and the code adapter, if any, that reads it. A connector is deactivated, never deleted.
/// </summary>
public sealed class ConnectorsController : Controller
{
    private readonly IConnectorsApiClient _connectors;
    private readonly IVendorsApiClient _vendors;
    private readonly IIntegrationsApiClient _integrations;
    private readonly ITransportsApiClient _transports;

    public ConnectorsController(IConnectorsApiClient connectors, IVendorsApiClient vendors, IIntegrationsApiClient integrations, ITransportsApiClient transports)
    {
        _connectors = connectors;
        _vendors = vendors;
        _integrations = integrations;
        _transports = transports;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new ConnectorsIndexViewModel { Connectors = await _connectors.ListAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
        => View(await WithOptionsAsync(new ConnectorFormViewModel(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(ConnectorFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _connectors.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Connector '{model.Input.DisplayName}' was created. Choose it when you add an integration.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(ConnectorFormViewModel.Input));
        }

        return View(await WithOptionsAsync(model, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _connectors.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(await WithOptionsAsync(new ConnectorFormViewModel { Id = id, Input = input }, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ConnectorFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _connectors.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Connector '{model.Input.DisplayName}' was saved.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(ConnectorFormViewModel.Input));
        }

        model.Id = id;
        return View(await WithOptionsAsync(model, cancellationToken));
    }

    private async Task<ConnectorFormViewModel> WithOptionsAsync(ConnectorFormViewModel model, CancellationToken cancellationToken)
    {
        model.Vendors = await _vendors.ListAsync(cancellationToken);
        model.PayloadFormats = await _integrations.ListPayloadFormatOptionsAsync(cancellationToken);
        model.Transports = await _transports.ListAsync(cancellationToken);
        return model;
    }

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
