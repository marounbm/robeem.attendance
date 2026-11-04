using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's review of records left for a person (<c>api/attendance/reviews</c>).</summary>
public interface IReviewsApiClient
{
    /// <summary>Lists one page of the records waiting for the acting user's review, oldest day first; the paging and the filters are done by the Api.</summary>
    /// <param name="employeeId">Only this employee.</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="reason">Only this reason: Overtime, UnscheduledWork, IncompleteDay or Review.</param>
    /// <param name="page">The 1-based page; a page past the end answers the last.</param>
    /// <param name="pageSize">Records per page.</param>
    Task<PagedResult<ReviewListItem>> ListPendingAsync(int? employeeId, DateTime? from, DateTime? to, string? reason, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The employees whose days the user may see, to narrow the queue to one of them.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Approves or rejects a record waiting for review as the acting user.</summary>
    Task<ApiCommandResult<ReviewDecisionResponse>> DecideAsync(int recordId, DecideReviewRequest request, CancellationToken cancellationToken);
}
