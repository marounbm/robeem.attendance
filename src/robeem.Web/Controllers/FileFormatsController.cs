using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The custom file formats: a layout an administrator defines (columns, delimiter, timestamp patterns,
/// type labels) that an integration can read its manual uploads by. A sample file can be tried before saving.
/// </summary>
public sealed class FileFormatsController : Controller
{
    private const int BlankCodeRows = 4;

    private readonly IFileFormatsApiClient _formats;

    public FileFormatsController(IFileFormatsApiClient formats)
    {
        _formats = formats;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new FileFormatsIndexViewModel { Formats = await _formats.ListAsync(cancellationToken) });

    [HttpGet]
    public IActionResult Create() => View(WithBlankCodeRows(new FileFormatFormViewModel()));

    [HttpPost]
    public async Task<IActionResult> Create(FileFormatFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _formats.CreateAsync(model.Input, cancellationToken);
            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"File format '{model.Input.Name}' was created. Choose it on an integration set to Manual file upload.";
                return RedirectToAction(nameof(Index));
            }

            AddErrors(result, nameof(FileFormatFormViewModel.Input));
        }

        return View(WithBlankCodeRows(model));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (await _formats.GetInputAsync(id, cancellationToken) is not { } input)
        {
            return NotFound();
        }

        return View(WithBlankCodeRows(new FileFormatFormViewModel { Id = id, Input = input }));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, FileFormatFormViewModel model, CancellationToken cancellationToken)
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

            AddErrors(result, nameof(FileFormatFormViewModel.Input));
        }

        model.Id = id;
        return View(WithBlankCodeRows(model));
    }

    /// <summary>Reads a sample file by the format as it stands in the form, saved or not, and shows the first rows. Stores nothing.</summary>
    [HttpPost]
    public async Task<IActionResult> Preview(int? id, FileFormatFormViewModel model, IFormFile? sample, CancellationToken cancellationToken)
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
    private static FileFormatFormViewModel WithBlankCodeRows(FileFormatFormViewModel model)
    {
        model.Input.EventCodes = model.Input.EventCodes.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        for (var i = 0; i < BlankCodeRows; i++)
        {
            model.Input.EventCodes.Add(new FileFormatEventCodeInput());
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
