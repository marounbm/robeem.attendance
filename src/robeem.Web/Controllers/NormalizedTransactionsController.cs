using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>
/// The normalized punches, interpreted from the raw ones, newest first. Read-only: a correction is a
/// new row elsewhere, never an edit here. Opening the screen is written to the audit log.
/// </summary>
public sealed class NormalizedTransactionsController : Controller
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;

    private readonly ITransactionsApiClient _transactions;

    public NormalizedTransactionsController(ITransactionsApiClient transactions)
    {
        _transactions = transactions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int take, CancellationToken cancellationToken)
    {
        var type = NormalizedTransactionsIndexViewModel.EventTypes.Contains(eventType ?? string.Empty) ? eventType : null;
        var repeat = NormalizedTransactionsIndexViewModel.DuplicateStatuses.Contains(duplicate ?? string.Empty) ? duplicate : null;
        var chosenFlag = NormalizedTransactionsIndexViewModel.Flags.Contains(flag ?? string.Empty) ? flag : null;
        var rows = take <= 0 ? DefaultTake : Math.Min(take, MaxTake);
        return View(new NormalizedTransactionsIndexViewModel
        {
            Rows = await _transactions.ListNormalizedAsync(integrationId, rawId, employee, type, repeat, chosenFlag, from, to, rows, cancellationToken),
            Integrations = await _transactions.ListIntegrationsAsync(cancellationToken),
            IntegrationId = integrationId,
            RawId = rawId,
            Employee = employee,
            EventType = type,
            Duplicate = repeat,
            Flag = chosenFlag,
            From = from,
            To = to,
            Take = rows,
        });
    }
}
