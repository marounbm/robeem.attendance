using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;

namespace robeem.Api.Controllers;

/// <summary>
/// Deletes all of the current tenant's attendance data so that it can be imported again. Only an integration administrator, only where the host
/// has turned it on (<c>Attendance:DataReset:Enabled</c>), and only with the tenant id typed. The deletion is audited in an entry that is kept.
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/data-reset")]
public sealed class AttendanceDataResetController : ControllerBase
{
    private readonly IAttendanceDataResetService _reset;

    public AttendanceDataResetController(IAttendanceDataResetService reset)
    {
        _reset = reset;
    }

    /// <summary>What a reset would delete and keep, and whether the host allows it.</summary>
    [HttpGet]
    public async Task<AttendanceDataSummary> Summary(CancellationToken cancellationToken) =>
        await _reset.GetSummaryAsync(cancellationToken);

    /// <summary>Deletes all attendance data of the current tenant. 409 with the reason when the host doesn't allow it or the tenant id was not typed.</summary>
    [HttpPost]
    public async Task<ActionResult<List<AttendanceDataCount>>> Reset(AttendanceDataResetRequest request, CancellationToken cancellationToken)
    {
        var result = await _reset.ResetAsync(request.Confirmation, cancellationToken);
        return result.Outcome switch
        {
            AttendanceDataResetOutcome.Done => result.Deleted.ToList(),
            AttendanceDataResetOutcome.Disabled => Problem(statusCode: StatusCodes.Status409Conflict, detail: "Deleting all attendance data is not turned on for this host (Attendance:DataReset:Enabled)."),
            _ => Problem(statusCode: StatusCodes.Status409Conflict, detail: "Type the tenant id exactly as shown to confirm."),
        };
    }
}
