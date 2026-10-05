using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

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

    [HttpGet]
    public async Task<IReadOnlyList<AuditLogEntry>> List(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken) =>
        await _audit.ListAsync(action, entityType, entityId, take == 0 ? 100 : take, cancellationToken);
}
