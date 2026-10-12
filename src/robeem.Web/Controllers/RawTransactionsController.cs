using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The raw punches, exactly as the vendors sent them, newest first. Read-only: a raw punch is
/// never changed. Opening the screen is written to the audit log.
/// </summary>
public sealed class RawTransactionsController : Controller
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private readonly ITransactionsApiClient _transactions;

    public RawTransactionsController(ITransactionsApiClient transactions)
    {
        _transactions = transactions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, int take, CancellationToken cancellationToken)
    {
        var chosen = RawTransactionsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status : null;
        var rows = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);
        return View(new RawTransactionsIndexViewModel
        {
            Rows = await _transactions.ListRawAsync(integrationId, batchId, chosen, from, to, search, rows, cancellationToken),
            Integrations = await _transactions.ListIntegrationsAsync(cancellationToken),
            IntegrationId = integrationId,
            BatchId = batchId,
            Status = chosen,
            From = from,
            To = to,
            Search = search,
            Take = rows,
        });
    }
}
