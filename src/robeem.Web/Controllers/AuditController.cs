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
    public async Task<IActionResult> Index(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = AuditIndexViewModel.PageSizes.Contains(pageSize) ? pageSize : AuditIndexViewModel.DefaultPageSize;
        var result = await _audit.ListAsync(action, entityType, entityId, Math.Max(page, 1), size, cancellationToken);
        return View(new AuditIndexViewModel
        {
            Entries = result.Items,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Page = result.Page,
            PageSize = size,
            Total = result.Total,
        });
    }
}
