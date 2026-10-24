using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's corrections of attendance exceptions (<c>api/attendance/corrections</c>).</summary>
public interface ICorrectionsApiClient
{
    /// <summary>Proposes the missing punch of the user's own open exception.</summary>
    Task<ApiCommandResult<CreatedResponse>> ProposeAsync(ProposeCorrectionRequest request, CancellationToken cancellationToken);

    /// <summary>Gets one of the user's own days with every punch, for correcting it; null when there is no such day for the user.</summary>
    Task<DayCorrectionView?> GetDayAsync(DateTime date, CancellationToken cancellationToken);

    /// <summary>Sends the corrections of one of the user's own days for approval, together.</summary>
    Task<ApiCommandResult<CreatedResponse>> ProposeDayAsync(DayCorrectionRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the corrections waiting for the manager's decision, or for HR's.</summary>
    Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover awaiting, CancellationToken cancellationToken);

    /// <summary>Approves or rejects a pending correction as the acting user.</summary>
    Task<ApiCommandResult<CorrectionDecisionResponse>> DecideAsync(int correctionId, DecideCorrectionRequest request, CancellationToken cancellationToken);
}
