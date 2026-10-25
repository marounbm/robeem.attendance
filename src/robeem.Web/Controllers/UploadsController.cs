using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Uploads an attendance file the clock exported. HR picks the integration, which fixes the file
/// format and the layout, sees that layout, and sends the file. Every row is stored as received.
/// </summary>
public sealed class UploadsController : Controller
{
    private const int HistorySize = 50;

    private readonly IUploadsApiClient _uploads;

    public UploadsController(IUploadsApiClient uploads)
    {
        _uploads = uploads;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, CancellationToken cancellationToken)
    {
        var targets = await _uploads.ListTargetsAsync(cancellationToken);

        // A single choice is chosen for the user; with several, nothing is until they pick one.
        var selected = integrationId is { } id ? targets.FirstOrDefault(t => t.IntegrationId == id) : targets.Count == 1 ? targets[0] : null;
        return View(new UploadViewModel { Targets = targets, Selected = selected, Uploads = await _uploads.ListUploadsAsync(HistorySize, cancellationToken) });
    }

    [HttpPost]
    public async Task<IActionResult> Upload(int integrationId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Choose a file to upload: it can't be empty.";
            return RedirectToAction(nameof(Index), new { integrationId });
        }

        // The file must be in a format the chosen integration's vendor reads; the Api would only store it as unusable rows.
        var target = (await _uploads.ListTargetsAsync(cancellationToken)).FirstOrDefault(t => t.IntegrationId == integrationId);
        if (target is null)
        {
            TempData["ErrorMessage"] = "That integration does not take file uploads.";
            return RedirectToAction(nameof(Index));
        }

        var extension = Path.GetExtension(file.FileName);
        if (target.FileExtensions.Count > 0 && !target.FileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = $"{target.DisplayName} reads {string.Join(", ", target.FileExtensions)} files; '{file.FileName}' is not one of them.";
            return RedirectToAction(nameof(Index), new { integrationId });
        }

        await using var stream = file.OpenReadStream();
        var result = await _uploads.UploadAsync(integrationId, stream, file.FileName, cancellationToken);
        if (result.Succeeded)
        {
            var batch = result.Value!;
            var text = $"Uploaded {file.FileName}: {batch.RecordsIngested} row(s) stored, {batch.RecordsFailed} unusable. Status: {batch.Status}.";
            text += string.IsNullOrWhiteSpace(batch.ErrorSummary) ? string.Empty : " " + batch.ErrorSummary;
            text += batch.Sha256 is null ? string.Empty : $" SHA-256 {batch.Sha256}.";

            // The rows were stored anyway (raw storage tolerates duplicates); normalization marks them as such.
            if (batch.SameFileUploadedAt is { } earlier)
            {
                text += $" This exact file was already uploaded on {DisplayFormats.Instant(earlier)}; its punches count once.";
            }

            // Every row unusable means the file is not in the layout this integration reads, not that a few rows are bad.
            if (batch.RecordsIngested > 0 && batch.RecordsFailed == batch.RecordsIngested)
            {
                text += $" Every row failed, so the file is probably not in the layout of {target.DisplayName} ({target.VendorName}): compare it with the layout above, or choose the integration of the vendor that matches the clock.";
            }

            TempData[batch.Status == "Failed" ? "ErrorMessage" : "StatusMessage"] = text;
        }
        else
        {
            TempData["ErrorMessage"] = result.MessageText;
        }

        return RedirectToAction(nameof(Index), new { integrationId });
    }
}
