namespace robeem.Application.Attendance;

/// <summary>
/// Turns raw transactions into normalized ones (study §7, §10.3-§10.4). Runs asynchronously from
/// ingestion (CLAUDE.md §1.3): only the background job calls it, never the ingestion path.
/// </summary>
public interface INormalizationService
{
    /// <summary>
    /// Takes up again the punches that were flagged because their employee or device could not be resolved, once it can be: a mapping
    /// was added on the device, or the device was registered. The old normalized punch is only taken out of the calculation (Excluded,
    /// kept as history) and its raw row goes back to Pending, so the next run writes a new punch (invariants 2 and 7). Returns how many
    /// punches were queued.
    /// </summary>
    Task<int> RequeueResolvablePunchesAsync(CancellationToken cancellationToken);

    /// <summary>Lists the batches that still have Pending raw transactions, oldest first.</summary>
    Task<IReadOnlyList<int>> ListBatchesWithPendingTransactionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Normalizes every Pending raw transaction of the batch. Each raw row ends up either
    /// Normalized, with exactly one normalized row (possibly flagged or a duplicate), or Failed,
    /// with no normalized row and the reason in <c>ProcessingError</c>. Each raw row is saved on its
    /// own, so an interrupted run resumes where it stopped.
    /// </summary>
    /// <exception cref="InvalidOperationException">No event interpreter is registered for a raw row's vendor; its rows stay Pending.</exception>
    Task<NormalizationResult> NormalizeBatchAsync(int batchId, CancellationToken cancellationToken);
}
