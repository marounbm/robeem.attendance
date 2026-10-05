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
    public async Task<IActionResult> Index(int? periodId, DateTime? from, DateTime? to, CancellationToken cancellationToken) =>
        View(await BuildAsync(periodId, from, to, cancellationToken));

    /// <summary>The same query as <see cref="Index"/>, as a CSV file with one line per employee day.</summary>
    [HttpGet]
    public async Task<IActionResult> Export(int? periodId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var model = await BuildAsync(periodId, from, to, cancellationToken);
        if (model.Days is null || model.From is not { } start || model.To is not { } end)
        {
            TempData["ErrorMessage"] = model.Error ?? "Choose a pay period or dates to export.";
            return RedirectToAction(nameof(Index));
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(PayrollCsv.Build(model.Days)))
            .ToArray();
        return File(bytes, "text/csv", $"payroll-{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.csv");
    }

    private async Task<PayrollIndexViewModel> BuildAsync(int? periodId, DateTime? from, DateTime? to, CancellationToken cancellationToken)
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

        var days = await _payroll.GetApprovedAsync(start.Value, end.Value, cancellationToken);
        return new PayrollIndexViewModel { Periods = periods, PeriodId = periodId, Period = period, From = start, To = end, Days = days };
    }
}
