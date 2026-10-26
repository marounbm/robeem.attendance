using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's read-only raw and normalized punches (<c>api/attendance/transactions</c>).</summary>
public interface ITransactionsApiClient
{
    /// <param name="status">Pending, Normalized, Failed or Skipped; null for any.</param>
    /// <param name="employee">Part of the employee's number or name (through the vendor id's mapping).</param>
    /// <param name="page">The 1-based page, newest first; a page past the end answers the last.</param>
    /// <param name="pageSize">Punches per page.</param>
    Task<PagedResult<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, string? employee, int page, int pageSize, CancellationToken cancellationToken);

    /// <param name="eventType">CheckIn, CheckOut, BreakOut, BreakIn, Unknown or Other; null for any.</param>
    /// <param name="duplicate">Unique, DuplicateConfirmed or DuplicateCandidate; null for any.</param>
    /// <param name="flag">None, FlaggedInvalidSequence, FlaggedUnrecognizedEmployee or FlaggedUnrecognizedDevice; null for any.</param>
    /// <param name="page">The 1-based page, newest first; a page past the end answers the last.</param>
    /// <param name="pageSize">Punches per page.</param>
    Task<PagedResult<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The integrations to filter by.</summary>
    Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken);
}
