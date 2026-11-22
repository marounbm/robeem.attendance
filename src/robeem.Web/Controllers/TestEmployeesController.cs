using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// Loads test employees from a CSV file: the employees, the time schedule and team of each, and the mapping of their numbers to a device, so that
/// a file of punches made for them is found. For integration administrators, where the host turns it on. The Api checks the whole file and says
/// which rows are wrong; this only asks and tells.
/// </summary>
public sealed class TestEmployeesController : Controller
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly ITestEmployeesApiClient _employees;

    public TestEmployeesController(ITestEmployeesApiClient employees)
    {
        _employees = employees;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new TestEmployeesViewModel { Summary = await _employees.GetSummaryAsync(cancellationToken) });

    /// <summary>A small example of the file, made from the columns the Api lists: a scheduled employee, an hourly one and one with only the required columns.</summary>
    [HttpGet]
    public IActionResult Example()
    {
        const string text =
            "employee_number,first_name,last_name,department,time_schedule,team,pay_basis,device_id,email,job_title,hire_date\r\n" +
            "T-001,Rita,Haddad,Sales,Standard Week,,Scheduled,SCN-1,rita@example.com,Account manager,2026-01-01\r\n" +
            "T-002,Karim,Nassar,Warehouse,On Demand (Hourly Staff),,Hourly,SCN-1,,Packer,2026-02-15\r\n" +
            "T-003,Maya,Khoury,,,,,,,,\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(text), "text/csv", "test-employees-example.csv");
    }

    [HttpPost]
    public async Task<IActionResult> Load(IFormFile? file, string? timeZone, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Choose a CSV file to load: it can't be empty.";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxBytes)
        {
            TempData["ErrorMessage"] = "That file is too big for test employees (at most 2 MB).";
            return RedirectToAction(nameof(Index));
        }

        var summary = await _employees.GetSummaryAsync(cancellationToken);
        await using var stream = file.OpenReadStream();
        var result = await _employees.LoadAsync(stream, file.FileName, string.IsNullOrWhiteSpace(timeZone) ? summary.DefaultTimeZone : timeZone.Trim(), cancellationToken);
        if (!result.Succeeded)
        {
            // Every wrong row, so the file can be fixed once.
            TempData["ErrorMessage"] = result.Messages.Count == 1 ? result.Messages[0] : $"Nothing was loaded: {result.Messages.Count} problem(s) in {file.FileName}. " + string.Join(" ", result.Messages);
            return RedirectToAction(nameof(Index));
        }

        var loaded = result.Value!;
        var text = $"Loaded {file.FileName}: {loaded.Created} employee(s) created, {loaded.Skipped} already there, {loaded.Assigned} put on a time schedule, {loaded.Mapped} mapped to a device.";
        text += loaded.DevicesCreated.Count > 0 ? $" Device(s) made: {string.Join(", ", loaded.DevicesCreated)}." : string.Empty;
        TempData["StatusMessage"] = text;
        return RedirectToAction(nameof(Index));
    }
}
