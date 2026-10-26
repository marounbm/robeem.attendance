using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// Read-only look at the raw and the normalized punches. Each call is a direct read of attendance
/// data outside the engine, so each is written to the audit log before it runs.
/// </summary>
public interface IAttendanceTransactionQuery
{
    /// <summary>
    /// Lists one page of raw punches, newest first, with the number that match on every page together. The page size is 1 to
    /// 200 (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Lists one page of normalized punches, newest first, with the number that match on every page together. The page size is
    /// 1 to 200 (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>Lists the integrations to filter by. Reads no punches, so it is not audited.</summary>
    Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken);
}
