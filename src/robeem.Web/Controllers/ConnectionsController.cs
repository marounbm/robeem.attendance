using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The connections of an integration: where its attendance comes from and how that is authenticated. A connection is switched off,
/// never deleted. Only the name of its secret is ever shown or typed; a test says whether it worked in the server's own words.
/// </summary>
public sealed class ConnectionsController : Controller
{
    private readonly IConnectionsApiClient _connections;
    private readonly IIntegrationsApiClient _integrations;

    public ConnectionsController(IConnectionsApiClient connections, IIntegrationsApiClient integrations)
    {
        _connections = connections;
        _integrations = integrations;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int integrationId, CancellationToken cancellationToken)
    {
        if (await _integrations.GetAsync(integrationId, cancellationToken) is not { } integration
            || await _connections.ListAsync(integrationId, cancellationToken) is not { } connections)
        {
            return NotFound();
        }

        return View(new ConnectionsIndexViewModel { Integration = integration, Connections = connections });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int integrationId, CancellationToken cancellationToken)
    {
        if (await _integrations.GetAsync(integrationId, cancellationToken) is not { } integration)
        {
            return NotFound();
        }

        var model = new ConnectionFormViewModel { Integration = integration, Input = new ConnectionInput { Name = string.Empty } };
        await FillAsync(model, cancellationToken);
        model.Input.Transport = model.Transports.FirstOrDefault(t => t.IsActive)?.Key ?? "FileUpload";
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int integrationId, ConnectionFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _connections.CreateAsync(integrationId, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Connection '{model.Input.Name}' was added.";
                return RedirectToAction(nameof(Index), new { integrationId });
            }

            AddErrors(result);
        }

        if (await _integrations.GetAsync(integrationId, cancellationToken) is not { } integration)
        {
            return NotFound();
        }

        model.Integration = integration;
        await FillAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int integrationId, int id, CancellationToken cancellationToken)
    {
        if (await _integrations.GetAsync(integrationId, cancellationToken) is not { } integration
            || await _connections.GetInputAsync(integrationId, id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        var model = new ConnectionFormViewModel { Id = id, Integration = integration, Input = input };
        await FillAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int integrationId, int id, ConnectionFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _connections.UpdateAsync(integrationId, id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Connection '{model.Input.Name}' was saved.";
                return RedirectToAction(nameof(Index), new { integrationId });
            }

            AddErrors(result);
        }

        if (await _integrations.GetAsync(integrationId, cancellationToken) is not { } integration)
        {
            return NotFound();
        }

        model.Id = id;
        model.Integration = integration;
        await FillAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> MakeDefault(int integrationId, int id, CancellationToken cancellationToken)
    {
        var result = await _connections.MakeDefaultAsync(integrationId, id, cancellationToken);
        if (result.NotFound)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "The connection is now the default." : result.MessageText;
        return RedirectToAction(nameof(Index), new { integrationId });
    }

    [HttpPost]
    public async Task<IActionResult> Test(int integrationId, int id, CancellationToken cancellationToken)
    {
        if (await _connections.TestAsync(integrationId, id, cancellationToken) is not { } result)
        {
            return NotFound();
        }

        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = (result.Succeeded ? "Test passed. " : "Test failed. ") + result.Message;
        return RedirectToAction(nameof(Index), new { integrationId });
    }

    private async Task FillAsync(ConnectionFormViewModel model, CancellationToken cancellationToken)
    {
        // The transports the integration's connector offers, and file upload.
        var connectors = await _integrations.ListConnectorsAsync(cancellationToken);
        model.Transports = connectors.FirstOrDefault(c => c.Id == model.Integration.ConnectorId)?.SupportedTransports ?? [];
        model.Descriptors = await _connections.ListDescriptorsAsync(cancellationToken);
    }

    private void AddErrors(ApiSaveResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{nameof(ConnectionFormViewModel.Input)}.{field}", message);
        }
    }
}
