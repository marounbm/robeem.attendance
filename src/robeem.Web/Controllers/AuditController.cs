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

    // The filter on the audit action is called auditAction, not action: a parameter called action is filled from the route's action
    // name ("Index"), and as a pager route value it would redirect the link to another action.
    [HttpGet]
    public async Task<IActionResult> Index(string? auditAction, string? entityType, int? entityId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = AuditIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : AuditIndexViewModel.DefaultPageSize;
        var result = await _audit.ListAsync(auditAction, entityType, entityId, from, to, Math.Max(page, 1), size, cancellationToken);
        return View(new AuditIndexViewModel
        {
            Entries = result.Items,
            Action = auditAction,
            EntityType = entityType,
            EntityId = entityId,
            From = from,
            To = to,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
        });
    }
}
