using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Deletes the employees named in a CSV file (the one test employees were loaded from) and everything of theirs, raw punches included. For integration
/// administrators, where the host turns it on; the tenant id is typed to confirm. The Api decides and audits; this only asks and tells.
/// </summary>
public sealed class DeleteTestEmployeesController : Controller
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly ITestEmployeesApiClient _employees;

    public DeleteTestEmployeesController(ITestEmployeesApiClient employees)
    {
        _employees = employees;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new TestEmployeesViewModel { Summary = await _employees.GetSummaryAsync(cancellationToken) });

    [HttpPost]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (TooBigOrMissing(file) is { } problem)
        {
            TempData["ErrorMessage"] = problem;
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file!.OpenReadStream();
        var result = await _employees.PreviewDeleteAsync(stream, file.FileName, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "Nothing was deleted yet. Deleting would remove " + Describe(result.Value!) + " Type the tenant id and press Delete to do it."
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(IFormFile? file, string? confirmation, CancellationToken cancellationToken)
    {
        if (TooBigOrMissing(file) is { } problem)
        {
            TempData["ErrorMessage"] = problem;
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file!.OpenReadStream();
        var result = await _employees.DeleteAsync(stream, file.FileName, confirmation ?? string.Empty, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "Deleted " + Describe(result.Value!)
            : result.MessageText;
        return RedirectToAction(nameof(Index));
    }

    private static string? TooBigOrMissing(IFormFile? file) =>
        file is null || file.Length == 0 ? "Choose the CSV file of the employees to delete: it can't be empty."
        : file.Length > MaxBytes ? "That file is too big (at most 2 MB)."
        : null;

    private static string Describe(TestEmployeeDeleteResult result)
    {
        var what = string.Join(", ", result.Counts.Where(c => c.Count > 0).Select(c => $"{c.Count} {c.Name.ToLowerInvariant()}"));
        var text = $"{result.Employees} employee(s) of the file, with: {what}.";
        return result.NotFound.Count > 0
            ? text + $" {result.NotFound.Count} number(s) of the file are no employee of this tenant and were left out ({string.Join(", ", result.NotFound.Take(10))}{(result.NotFound.Count > 10 ? ", ..." : string.Empty)})."
            : text;
    }
}
