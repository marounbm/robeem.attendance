using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The catalog of attendance vendors: the companies, with a key, a name and a website. How a vendor's data arrives and is read
/// belongs to its connectors (the Connectors screen). A vendor is deactivated, never deleted.
/// </summary>
public sealed class VendorsController : Controller
{
    private readonly IVendorsApiClient _vendors;

    public VendorsController(IVendorsApiClient vendors)
    {
        _vendors = vendors;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new VendorsIndexViewModel { Vendors = await _vendors.ListAsync(cancellationToken) });

    [HttpGet]
    public IActionResult Create()
        => View(new VendorFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(VendorFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _vendors.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Vendor '{model.Input.DisplayName}' was created. Add a connector to say how its data arrives.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(VendorFormViewModel.Input));
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _vendors.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(new VendorFormViewModel { Id = id, Input = input });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, VendorFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _vendors.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"Vendor '{model.Input.DisplayName}' was saved.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(VendorFormViewModel.Input));
        }

        model.Id = id;
        return View(model);
    }

    private void AddErrors(ApiSaveResult result, string prefix)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : $"{prefix}.{field}", message);
        }
    }
}
