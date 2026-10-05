using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's review of records left for a person (<c>api/attendance/reviews</c>).</summary>
public interface IReviewsApiClient
{
    /// <summary>Lists the records waiting for the acting user's review.</summary>
    Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>Approves or rejects a record waiting for review as the acting user.</summary>
    Task<ApiCommandResult<ReviewDecisionResponse>> DecideAsync(int recordId, DecideReviewRequest request, CancellationToken cancellationToken);
}
