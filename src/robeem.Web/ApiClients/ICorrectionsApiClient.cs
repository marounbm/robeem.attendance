using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's corrections of attendance exceptions (<c>api/attendance/corrections</c>).</summary>
public interface ICorrectionsApiClient
{
    /// <summary>Proposes the missing punch of the user's own open exception.</summary>
    Task<ApiCommandResult<CreatedResponse>> ProposeAsync(ProposeCorrectionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a day with every punch, for correcting it: the user's own, or another employee's when <paramref name="employeeId"/> is given
    /// (for their manager or HR). Null when there is no such day for the user.
    /// </summary>
    Task<DayCorrectionView?> GetDayAsync(DateTime date, int? employeeId, CancellationToken cancellationToken);

    /// <summary>Sends the corrections of a day for approval, together: the user's own, or the employee the request names.</summary>
    Task<ApiCommandResult<CreatedResponse>> ProposeDayAsync(DayCorrectionRequest request, CancellationToken cancellationToken);
}
