using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.Actors;
using robeem.Web.ApiClients;
using robeem.Web.Models;
using robeem.Web.Navigation;
using robeem.Web.Tenancy;

namespace robeem.Web.Controllers;

/// <summary>
/// The payload formats: a layout (columns, delimiter, timestamp patterns, type labels) that an integration reads its files by. They
/// belong to the prime tenant and are the same for every tenant. Any tenant's integration administrator lists and views them and
/// downloads an example file; a platform administrator in the prime tenant defines and changes them, and can try a sample file on a
/// format before saving (the actions marked <see cref="ChangesPlatformCatalogAttribute"/>).
/// </summary>
public sealed class PayloadFormatsController : Controller
{
    private const int BlankCodeRows = 4;

    private readonly IPayloadFormatsApiClient _formats;
    private readonly ICurrentActor? _actor;
    private readonly ICurrentTenantInfo? _tenant;

    public PayloadFormatsController(IPayloadFormatsApiClient formats, ICurrentActor? actor = null, ICurrentTenantInfo? tenant = null)
    {
        _formats = formats;
        _actor = actor;
        _tenant = tenant;
    }

    // Only to decide what to show: the Api refuses a change from anyone else. Unknown (no actor or tenant info) shows it.
    private bool CanManage => _tenant?.IsPrime != false && (_actor?.IsInRole(NavigationMenu.Platform) ?? true);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new PayloadFormatsIndexViewModel { Formats = await _formats.ListAsync(cancellationToken), CanManage = CanManage });

    /// <summary>A format as the tenant sees it: read-only, with example files to download.</summary>
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (await _formats.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(new PayloadFormatDetailsViewModel { Id = id, Format = input, CanManage = CanManage });
    }

    /// <summary>Sends an example file (CSV, JSON or XML) made from a saved format. Open to every tenant: it stores and reads nothing.</summary>
    [HttpGet]
    public async Task<IActionResult> Download(int id, string kind, CancellationToken cancellationToken)
    {
        if (!new[] { "csv", "json", "xml" }.Contains((kind ?? string.Empty).Trim().ToLowerInvariant()))
        {
            return BadRequest("The kind of example must be csv, json or xml.");
        }

        return await _formats.SampleOfSavedAsync(id, kind!, cancellationToken) is { } sample
            ? File(System.Text.Encoding.UTF8.GetBytes(sample.Content), sample.ContentType, sample.FileName)
            : NotFound();
    }

    [HttpGet]
    [ChangesPlatformCatalog]
    public IActionResult Create() => View(WithBlankCodeRows(new PayloadFormatFormViewModel()));

    [HttpPost]
    [ChangesPlatformCatalog]
    public async Task<IActionResult> Create(PayloadFormatFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _formats.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"File format '{model.Input.Name}' was created. Choose it on an integration set to Manual file upload.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(PayloadFormatFormViewModel.Input));
        }

        return View(WithBlankCodeRows(model));
    }

    [HttpGet]
    [ChangesPlatformCatalog]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _formats.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(WithBlankCodeRows(new PayloadFormatFormViewModel { Id = id, Input = input }));
    }

    [HttpPost]
    [ChangesPlatformCatalog]
    public async Task<IActionResult> Edit(int id, PayloadFormatFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _formats.UpdateAsync(id, model.Input, cancellationToken);
            if (result.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"File format '{model.Input.Name}' was saved.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(PayloadFormatFormViewModel.Input));
        }

        model.Id = id;
        return View(WithBlankCodeRows(model));
    }

    /// <summary>
    /// An example file (CSV, JSON or XML) made from the format as it stands in the form, saved or not: shown on the page when
    /// <paramref name="download"/> is false, otherwise sent as a file. The form is never validated here: an unfinished format still gets
    /// an example, with default names for what is not filled in. Stores nothing.
    /// </summary>
    [HttpPost]
    [ChangesPlatformCatalog]
    public async Task<IActionResult> Sample(PayloadFormatFormViewModel model, string kind, bool download, CancellationToken cancellationToken)
    {
        if (!new[] { "csv", "json", "xml" }.Contains((kind ?? string.Empty).Trim().ToLowerInvariant()))
        {
            return BadRequest("The kind of example must be csv, json or xml.");
        }

        var sample = await _formats.SampleAsync(model.Input, kind!, cancellationToken);
        return download
            ? File(System.Text.Encoding.UTF8.GetBytes(sample.Content), sample.ContentType, sample.FileName)
            : Content(sample.Content, "text/plain; charset=utf-8");
    }

    /// <summary>Reads a sample file by the format as it stands in the form, saved or not, and shows the first rows. Stores nothing.</summary>
    [HttpPost]
    [ChangesPlatformCatalog]
    public async Task<IActionResult> Preview(int? id, PayloadFormatFormViewModel model, IFormFile? sample, CancellationToken cancellationToken)
    {
        model.Id = id;
        if (sample is null || sample.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose a sample file to try the format on.");
        }
        else
        {
            await using var stream = sample.OpenReadStream();
            var result = await _formats.PreviewAsync(model.Input, stream, sample.FileName, cancellationToken);
            if (result.Succeeded)
            {
                model.Preview = result.Value;
            }
            else
            {
                ModelState.AddModelError(string.Empty, result.MessageText);
            }
        }

        return View(id is null ? nameof(Create) : nameof(Edit), WithBlankCodeRows(model));
    }

    // The form shows a few spare label rows after the filled ones; the indexes stay contiguous so the model binder reads them all.
    // The view writes these inputs by hand, so a value the user posted is not shadowed by the model state of a shifted index.
    private static PayloadFormatFormViewModel WithBlankCodeRows(PayloadFormatFormViewModel model)
    {
        model.Input.EventCodes = model.Input.EventCodes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        for (var i = 0; i < BlankCodeRows; i++)
        {
            model.Input.EventCodes.Add(new PayloadFormatEventCodeInput());
        }

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
