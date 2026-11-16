using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's deletion of all attendance data (<c>api/attendance/data-reset</c>): what it would delete, and doing it.</summary>
public interface IDataResetApiClient
{
    Task<AttendanceDataSummary> GetSummaryAsync(CancellationToken cancellationToken);

    /// <summary>Deletes all attendance data of the current tenant. Refused, with the reason, when the host doesn't allow it or the tenant id was not typed.</summary>
    Task<ApiCommandResult<IReadOnlyList<AttendanceDataCount>>> ResetAsync(string confirmation, CancellationToken cancellationToken);
}
