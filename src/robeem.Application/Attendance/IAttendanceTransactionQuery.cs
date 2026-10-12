using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Read-only look at the raw and the normalized punches. Each call is a direct read of attendance
/// data outside the engine, so each is written to the audit log before it runs.
/// </summary>
public interface IAttendanceTransactionQuery
{
    /// <summary>Lists raw punches, newest first, at most <see cref="RawTransactionFilter.Take"/> (1 to 500).</summary>
    Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>Lists normalized punches, newest first, at most <see cref="NormalizedTransactionFilter.Take"/> (1 to 500).</summary>
    Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>Lists the integrations to filter by. Reads no punches, so it is not audited.</summary>
    Task<IReadOnlyList<TransactionIntegrationOption>> ListIntegrationsAsync(CancellationToken cancellationToken);
}
