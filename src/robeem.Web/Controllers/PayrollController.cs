using System.Text;
using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// What payroll may pay for: the approved, exception-free days of a pay period or of free dates, of everyone or of one employee.
/// A query needs an employee, a period or dates, or both; with only an employee it reads every date of that employee.
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
    public async Task<IActionResult> Index(int? periodId, DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken)
    {
        var range = await ResolveAsync(periodId, from, to, Clean(employee), cancellationToken);
        if (!range.CanQuery)
        {
            return View(range);
        }

        // One page of the days, with the totals per employee over all of them; the CSV is the whole range.
        var size = PayrollIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : PayrollIndexViewModel.DefaultPageSize;
        var result = await _payroll.GetApprovedPageAsync(range.From, range.To, range.Employee, page <= 0 ? 1 : page, size, cancellationToken);
        return View(new PayrollIndexViewModel
        {
            Periods = range.Periods,
            PeriodId = range.PeriodId,
            Period = range.Period,
            From = range.From,
            To = range.To,
            Employee = range.Employee,
            Days = result.Days.Items,
            Employees = result.Employees,
            Page = result.Days.Page,
            PageSize = size,
            Total = result.Days.Total,
        });
    }

    /// <summary>The same query as <see cref="Index"/>, as a CSV file with one line per employee day: every day, not one page.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(int? periodId, DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken)
    {
        var range = await ResolveAsync(periodId, from, to, Clean(employee), cancellationToken);
        if (!range.CanQuery)
        {
            TempData["ErrorMessage"] = range.Error ?? "Choose an employee, a pay period or dates to export.";
            return RedirectToAction(nameof(Index));
        }

        var days = await _payroll.GetApprovedAsync(range.From, range.To, range.Employee, cancellationToken);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(PayrollCsv.Build(days)))
            .ToArray();
        var name = range.From is { } start && range.To is { } end ? $"{start:yyyy-MM-dd}-{end:yyyy-MM-dd}" : "all-dates";
        return File(bytes, "text/csv", $"payroll-{name}.csv");
    }

    private static string? Clean(string? employee) => string.IsNullOrWhiteSpace(employee) ? null : employee.Trim();

    /// <summary>Works out the dates asked for (a pay period's, else the free ones); the model has no days yet, or says why there can be none.</summary>
    private async Task<PayrollIndexViewModel> ResolveAsync(int? periodId, DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken)
    {
        var periods = await _periods.ListAsync(cancellationToken);
        var period = periodId is { } id ? periods.FirstOrDefault(p => p.Id == id) : null;
        if (periodId is not null && period is null)
        {
            return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, Employee = employee, Error = "That pay period doesn't exist." };
        }

        var start = period?.StartDate ?? from?.Date;
        var end = period?.EndDate ?? to?.Date;
        if (start is null != end is null)
        {
            return new PayrollIndexViewModel { Periods = periods, From = start, To = end, Employee = employee, Error = "Give both dates, or leave both empty to look at every date of one employee." };
        }

        if (end < start)
        {
            return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, From = start, To = end, Employee = employee, Error = "The period ends before it starts." };
        }

        return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, Period = period, From = start, To = end, Employee = employee };
    }
}
