using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The raw punches, exactly as the vendors sent them, newest first, a page at a time (the paging is done by robeem.Api).
/// Read-only: a raw punch is never changed. Opening the screen, and each page of it, is written to the audit log.
/// </summary>
public sealed class RawTransactionsController : Controller
{
    private readonly ITransactionsApiClient _transactions;

    public RawTransactionsController(ITransactionsApiClient transactions)
    {
        _transactions = transactions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, string? employee, int page, int pageSize, CancellationToken cancellationToken)
    {
        var chosen = RawTransactionsIndexViewModel.Statuses.Contains(status ?? string.Empty) ? status : null;
        var size = RawTransactionsIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : RawTransactionsIndexViewModel.DefaultPageSize;
        var result = await _transactions.ListRawAsync(integrationId, batchId, chosen, from, to, search, employee, Math.Max(page, 1), size, cancellationToken);
        return View(new RawTransactionsIndexViewModel
        {
            Rows = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Integrations = await _transactions.ListIntegrationsAsync(cancellationToken),
            IntegrationId = integrationId,
            BatchId = batchId,
            Status = chosen,
            From = from,
            To = to,
            Search = search,
            Employee = employee,
        });
    }
}
