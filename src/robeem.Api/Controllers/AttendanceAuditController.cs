using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Api.Controllers;

/// <summary>The attendance audit trail, newest first. Reading it is not itself recorded.</summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.HRAdministrator)]
[Route("api/attendance/audit")]
public sealed class AttendanceAuditController : ControllerBase
{
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceAuditController(IAttendanceAuditTrail audit)
    {
        _audit = audit;
    }

    /// <param name="page">The 1-based page, newest entries first (default 1). A page past the end answers the last page.</param>
    /// <param name="pageSize">Entries per page (default 20, at most 200).</param>
    [HttpGet]
    public async Task<PagedResult<AuditLogEntry>> List(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken) =>
        await _audit.ListAsync(action, entityType, entityId, page <= 0 ? 1 : page, pageSize, cancellationToken);
}
