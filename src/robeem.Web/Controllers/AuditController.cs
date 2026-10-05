using Microsoft.AspNetCore.Mvc;
using robeem.Web.ApiClients;
using robeem.Web.Models;

namespace robeem.Web.Controllers;

/// <summary>The attendance audit log, newest first: decisions, status changes, setup changes and reads of raw attendance.</summary>
public sealed class AuditController : Controller
{
    private readonly IAuditApiClient _audit;

    public AuditController(IAuditApiClient audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? action, string? entityType, int? entityId, int? take, CancellationToken cancellationToken)
    {
        var limit = take is > 0 and <= 500 ? take.Value : AuditIndexViewModel.DefaultTake;
        var entries = await _audit.ListAsync(action, entityType, entityId, limit, cancellationToken);
        return View(new AuditIndexViewModel
        {
            Entries = entries,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Take = limit,
        });
    }
}
