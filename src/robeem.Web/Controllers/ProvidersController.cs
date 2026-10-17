using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The catalog of attendance providers: a name, the transports it supports, and the file format its uploads are read by
/// (or, for an API or webhook vendor, the code adapter registered under its key). A provider is deactivated, never deleted.
/// </summary>
public sealed class ProvidersController : Controller
{
    private readonly IProvidersApiClient _providers;
    private readonly IIntegrationsApiClient _integrations;
    private readonly ITransportsApiClient _transports;

    public ProvidersController(IProvidersApiClient providers, IIntegrationsApiClient integrations, ITransportsApiClient transports)
    {
        _providers = providers;
        _integrations = integrations;
        _transports = transports;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new ProvidersIndexViewModel { Providers = await _providers.ListAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
        => View(await WithOptionsAsync(new ProviderFormViewModel(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(ProviderFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _providers.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Provider '{model.Input.DisplayName}' was created. Choose it when you add an integration.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(ProviderFormViewModel.Input));
        }

        return View(await WithOptionsAsync(model, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _providers.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(await WithOptionsAsync(new ProviderFormViewModel { Id = id, Input = input }, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ProviderFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _providers.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Provider '{model.Input.DisplayName}' was saved.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(ProviderFormViewModel.Input));
        }

        model.Id = id;
        return View(await WithOptionsAsync(model, cancellationToken));
    }

    private async Task<ProviderFormViewModel> WithOptionsAsync(ProviderFormViewModel model, CancellationToken cancellationToken)
    {
        model.FileFormats = await _integrations.ListFileFormatOptionsAsync(cancellationToken);
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
