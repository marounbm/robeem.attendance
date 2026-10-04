namespace robeem.Application.Attendance;

/// <summary>
/// Turns raw transactions into normalized ones (study §7, §10.3-§10.4). Runs asynchronously from
/// ingestion (CLAUDE.md §1.3): only the background job calls it, never the ingestion path.
/// </summary>
public interface INormalizationService
{
    /// <summary>Lists the batches that still have Pending raw transactions, oldest first.</summary>
    Task<IReadOnlyList<int>> ListBatchesWithPendingTransactionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Normalizes every Pending raw transaction of the batch. Each raw row ends up either
    /// Normalized, with exactly one normalized row (possibly flagged or a duplicate), or Failed,
    /// with no normalized row and the reason in <c>ProcessingError</c>. Each raw row is saved on its
    /// own, so an interrupted run resumes where it stopped.
    /// </summary>
    /// <exception cref="InvalidOperationException">No event interpreter is registered for a raw row's provider; its rows stay Pending.</exception>
    Task<NormalizationResult> NormalizeBatchAsync(int batchId, CancellationToken cancellationToken);
}
