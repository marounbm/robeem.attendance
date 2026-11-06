using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's approvals (<c>api/attendance/approvals</c>): corrections and reviews, listed and decided together.</summary>
public interface IApprovalsApiClient
{
    /// <summary>Lists one page of what waits for the acting user's decision, oldest day first; the paging and the filters are done by the Api.</summary>
    /// <param name="employeeId">Only this employee.</param>
    /// <param name="from">First attendance date, inclusive.</param>
    /// <param name="to">Last attendance date, inclusive.</param>
    /// <param name="type">Only this kind: Correction or Review.</param>
    /// <param name="reason">Only reviews with this reason: Overtime, UnscheduledWork, IncompleteDay or Review.</param>
    /// <param name="page">The 1-based page; a page past the end answers the last.</param>
    /// <param name="pageSize">Approvals per page.</param>
    Task<PagedResult<ApprovalListItem>> ListPendingAsync(int? employeeId, DateTime? from, DateTime? to, string? type, string? reason, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The employees whose days the user may see, to narrow the queue to one of them.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Approves or rejects an approval as the acting user.</summary>
    Task<ApiCommandResult<ApprovalDecisionResponse>> DecideAsync(int approvalId, DecideApprovalRequest request, CancellationToken cancellationToken);
}
