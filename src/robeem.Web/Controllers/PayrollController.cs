using System.Text;
using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// What payroll may pay for: the approved, exception-free days of a pay period or of free dates.
/// The page and the CSV read only the payroll gate; nothing else about attendance is shown here.
/// </summary>
public sealed class PayrollController : Controller
{
    private readonly IPayrollApiClient _payroll;
    private readonly IPayPeriodsApiClient _periods;

    public PayrollController(IPayrollApiClient payroll, IPayPeriodsApiClient periods)
    {
        _payroll = payroll;
        _periods = periods;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? periodId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var range = await ResolveAsync(periodId, from, to, cancellationToken);
        if (range.Error is not null || range.From is not { } start || range.To is not { } end)
        {
            return View(range);
        }

        // One page of the days, with the totals per employee over all of them; the CSV is the whole range.
        var size = PayrollIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : PayrollIndexViewModel.DefaultPageSize;
        var result = await _payroll.GetApprovedPageAsync(start, end, page <= 0 ? 1 : page, size, cancellationToken);
        return View(new PayrollIndexViewModel
        {
            Periods = range.Periods,
            PeriodId = range.PeriodId,
            Period = range.Period,
            From = start,
            To = end,
            Days = result.Days.Items,
            Employees = result.Employees,
            Page = result.Days.Page,
            PageSize = size,
            Total = result.Days.Total,
        });
    }

    /// <summary>The same query as <see cref="Index"/>, as a CSV file with one line per employee day: every day, not one page.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(int? periodId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var range = await ResolveAsync(periodId, from, to, cancellationToken);
        if (range.Error is not null || range.From is not { } start || range.To is not { } end)
        {
            TempData["ErrorMessage"] = range.Error ?? "Choose a pay period or dates to export.";
            return RedirectToAction(nameof(Index));
        }

        var days = await _payroll.GetApprovedAsync(start, end, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(PayrollCsv.Build(days)))
            .ToArray();
        return File(bytes, "text/csv", $"payroll-{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.csv");
    }

    /// <summary>Works out the dates asked for (a pay period's, else the free ones); the model has no days yet, or says why there can be none.</summary>
    private async Task<PayrollIndexViewModel> ResolveAsync(int? periodId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var periods = await _periods.ListAsync(cancellationToken);
        var period = periodId is { } id ? periods.FirstOrDefault(p => p.Id == id) : null;
        if (periodId is not null && period is null)
        {
            return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, Error = "That pay period doesn't exist." };
        }

        var start = period?.StartDate ?? from?.Date;
        var end = period?.EndDate ?? to?.Date;
        if (start is null || end is null)
        {
            return new PayrollIndexViewModel { Periods = periods, From = start, To = end };
        }

        if (end < start)
        {
            return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, From = start, To = end, Error = "The period ends before it starts." };
        }

        return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, Period = period, From = start, To = end };
    }
}
