using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The normalized punches, interpreted from the raw ones, newest first, a page at a time (the paging is done by robeem.Api).
/// Read-only: a correction is a new row elsewhere, never an edit here. Opening the screen, and each page of it, is written to the audit log.
/// </summary>
public sealed class NormalizedTransactionsController : Controller
{
    private readonly ITransactionsApiClient _transactions;

    public NormalizedTransactionsController(ITransactionsApiClient transactions)
    {
        _transactions = transactions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var type = NormalizedTransactionsIndexViewModel.EventTypes.Contains(eventType ?? string.Empty) ? eventType : null;
        var repeat = NormalizedTransactionsIndexViewModel.DuplicateStatuses.Contains(duplicate ?? string.Empty) ? duplicate : null;
        var chosenFlag = NormalizedTransactionsIndexViewModel.Flags.Contains(flag ?? string.Empty) ? flag : null;
        var size = NormalizedTransactionsIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : NormalizedTransactionsIndexViewModel.DefaultPageSize;
        var result = await _transactions.ListNormalizedAsync(integrationId, rawId, employee, type, repeat, chosenFlag, from, to, Math.Max(page, 1), size, cancellationToken);
        return View(new NormalizedTransactionsIndexViewModel
        {
            Rows = result.Items,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
            Integrations = await _transactions.ListIntegrationsAsync(cancellationToken),
            IntegrationId = integrationId,
            RawId = rawId,
            Employee = employee,
            EventType = type,
            Duplicate = repeat,
            Flag = chosenFlag,
            From = from,
            To = to,
        });
    }
}
