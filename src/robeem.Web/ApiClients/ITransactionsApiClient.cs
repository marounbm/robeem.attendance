using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's read-only raw and normalized punches (<c>api/attendance/transactions</c>).</summary>
public interface ITransactionsApiClient
{
    /// <param name="status">Pending, Normalized, Failed or Skipped; null for any.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(int? integrationId, int? batchId, string? status, DateTime? from, DateTime? to, string? search, int take, CancellationToken cancellationToken);

    /// <param name="eventType">CheckIn, CheckOut, BreakOut, BreakIn, Unknown or Other; null for any.</param>
    /// <param name="duplicate">Unique, DuplicateConfirmed or DuplicateCandidate; null for any.</param>
    /// <param name="flag">None, FlaggedInvalidSequence, FlaggedUnrecognizedEmployee or FlaggedUnrecognizedDevice; null for any.</param>
    /// <param name="take">At most this many, newest first.</param>
    Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(int? integrationId, int? rawId, string? employee, string? eventType, string? duplicate, string? flag, DateTime? from, DateTime? to, int take, CancellationToken cancellationToken);

    /// <summary>The integrations to filter by.</summary>
    Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken);
}
