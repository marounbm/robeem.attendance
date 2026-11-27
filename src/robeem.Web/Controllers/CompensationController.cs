using System.Text;
using Microsoft.AspNetCore.Mvc;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The compensation report: each employee's overtime, under time and penalties for their own pay period, decided line by line by the
/// payroll administrator. The paging, the periods and the decisions are done by robeem.Api; a leave decision adds leaves to the employee's leaves.
/// </summary>
public sealed class CompensationController : Controller
{
    private readonly ICompensationApiClient _compensation;

    public CompensationController(ICompensationApiClient compensation)
    {
        _compensation = compensation;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken)
    {
        var chosen = Mode(mode);
        var size = CompensationIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : CompensationIndexViewModel.DefaultPageSize;
        var result = await _compensation.ListAsync(Clean(search), chosen, date, page <= 0 ? 1 : page, size, cancellationToken);
        return View(new CompensationIndexViewModel
        {
            Items = result.Items,
            Search = Clean(search),
            Mode = chosen,
            Date = date,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
        });
    }

    /// <summary>One employee's report: the lines to decide, the totals, the balance and the one overtime line.</summary>
    [HttpGet]
    public async Task<IActionResult> Employee(int id, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var chosen = Mode(mode);
        return await _compensation.GetReportAsync(id, chosen, date, cancellationToken) is { } report
            ? View(new CompensationEmployeeViewModel { Report = report, Mode = chosen, Date = date })
            : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Decide(CompensationDecideForm form, CancellationToken cancellationToken)
    {
        var request = form.ToRequest();
        if (request.Lines.Count == 0 && request.UndecidedUndertime is null)
        {
            TempData["ErrorMessage"] = "Choose a decision for at least one line.";
        }
        else
        {
            var result = await _compensation.DecideAsync(request, cancellationToken);
            TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
                ? "The decisions were recorded. A leave decision added leaves to the employee's leaves, and the day was calculated again."
                : result.MessageText;
        }

        return BackToReport(form.EmployeeId, form.Mode, form.Date);
    }

    [HttpPost]
    public async Task<IActionResult> Close(int employeeId, DateTime periodStart, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var result = await _compensation.CloseAsync(new CompensationCloseRequest { EmployeeId = employeeId, PeriodStart = periodStart.Date }, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded
            ? "The pay period was closed: payroll no longer reads its days, and its decisions are locked."
            : result.MessageText;
        return BackToReport(employeeId, mode, date);
    }

    /// <summary>Closes the pay period shown for every employee of the overview that is ready; the others stay as they are, and the message says why.</summary>
    [HttpPost]
    public async Task<IActionResult> CloseAll(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var chosen = Mode(mode);
        var result = await _compensation.CloseAllAsync(
            new CompensationCloseAllRequest { Search = Clean(search), Mode = chosen, Date = chosen == CompensationIndexViewModel.Containing ? date : null },
            cancellationToken);

        var left = result.NotEnded + result.NotReady + result.Refused;
        var message = new StringBuilder($"{result.Closed} pay period(s) were closed: payroll no longer reads their days and their decisions are locked.");
        if (result.AlreadyClosed > 0)
        {
            message.Append($" {result.AlreadyClosed} were closed already.");
        }

        if (result.NothingToClose > 0)
        {
            message.Append($" {result.NothingToClose} had nothing to pay for.");
        }

        if (left > 0)
        {
            message.Append($" {left} were left open: ")
                .Append(string.Join("; ", result.Skipped.Take(5).Select(s => $"{s.EmployeeName} ({s.EmployeeNumber}), {s.Reason}")))
                .Append(left > 5 ? "; …" : ".");
        }

        TempData[left > 0 && result.Closed == 0 ? "ErrorMessage" : "StatusMessage"] = message.ToString();
        return RedirectToAction(nameof(Index), new { search = Clean(search), mode = chosen, date = chosen == CompensationIndexViewModel.Containing ? date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : null });
    }

    [HttpPost]
    public async Task<IActionResult> Reopen(int employeeId, DateTime periodStart, string? reason, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        // The Api refuses an empty reason too; checking here words it for the form.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Give a reason to reopen a closed pay period.";
            return BackToReport(employeeId, mode, date);
        }

        var result = await _compensation.ReopenAsync(new CompensationReopenRequest { EmployeeId = employeeId, PeriodStart = periodStart.Date, Reason = reason.Trim() }, cancellationToken);
        TempData[result.Succeeded ? "StatusMessage" : "ErrorMessage"] = result.Succeeded ? "The pay period was reopened. The reason is in the audit log." : result.MessageText;
        return BackToReport(employeeId, mode, date);
    }

    /// <summary>The same view as <see cref="Index"/>, as a CSV file with one line per employee: all of them, not one page.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var items = await _compensation.ListAllAsync(Clean(search), Mode(mode), date, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(CompensationCsv.Build(items)))
            .ToArray();
        return File(bytes, "text/csv", "compensation.csv");
    }

    /// <summary>
    /// Every day of every employee of the overview as a CSV file: the days payroll may pay for, for each employee's own period, with what was decided
    /// about them. It is what the old Payroll list was, with the decisions; closed periods are in it.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportDays(string? search, string? mode, DateTime? date, CancellationToken cancellationToken)
    {
        var rows = await _compensation.ListDaysAsync(Clean(search), Mode(mode), date, cancellationToken);
        return Csv(CompensationDaysCsv.Build(rows), "compensation-days.csv");
    }

    /// <summary>The days of one employee's report as a CSV file.</summary>
    [HttpGet]
    public async Task<IActionResult> ExportEmployeeDays(int id, string? mode, DateTime? date, CancellationToken cancellationToken) =>
        await _compensation.GetReportAsync(id, Mode(mode), date, cancellationToken) is { } report
            ? Csv(CompensationDaysCsv.Build(report), $"compensation-days-{report.EmployeeNumber}-{report.PeriodStart:yyyy-MM-dd}.csv")
            : NotFound();

    private static FileContentResult Csv(string text, string name) =>
        new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(), "text/csv") { FileDownloadName = name };

    private static string Mode(string? mode) =>
        string.Equals(mode, CompensationIndexViewModel.Containing, StringComparison.OrdinalIgnoreCase) ? CompensationIndexViewModel.Containing
        : string.Equals(mode, CompensationIndexViewModel.Last, StringComparison.OrdinalIgnoreCase) ? CompensationIndexViewModel.Last
        : CompensationIndexViewModel.Latest;

    private static string? Clean(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    private RedirectToActionResult BackToReport(int employeeId, string? mode, DateTime? date) =>
        RedirectToAction(nameof(Employee), new { id = employeeId, mode = Mode(mode), date = date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
}
