using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's leave endpoints (<c>api/leaves</c>).</summary>
public interface ILeavesApiClient
{
    /// <summary>Lists one page of leaves, newest first; a page past the end answers the last.</summary>
    Task<PagedResult<LeaveListItem>> ListAsync(int? employeeId, string? search, DateTime? from, DateTime? to, string? type, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Gets a leave, or <c>null</c> when it does not exist.</summary>
    Task<LeaveListItem?> GetAsync(int id, CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(LeaveInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, LeaveInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
