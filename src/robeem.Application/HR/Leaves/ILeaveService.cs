using robeem.Application.Common;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Leaves;

namespace robeem.Application.HR.Leaves;

public interface ILeaveService
{
    /// <summary>
    /// Lists one page of leaves, newest first (by first day, then id), with the number that match on every page together. The page size
    /// is 1 to 200 (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<LeaveListItem>> ListAsync(LeaveFilter filter, CancellationToken cancellationToken);

    Task<LeaveListItem?> GetAsync(int id, CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(LeaveInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateAsync(int id, LeaveInput input, CancellationToken cancellationToken);

    Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
