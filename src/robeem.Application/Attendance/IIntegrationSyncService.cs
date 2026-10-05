namespace robeem.Application.Attendance;

/// <summary>
/// Synchronizes one integration of the current tenant with its provider (study §12.4). Neither
/// method throws for a provider or storage failure: that is the outcome, so one integration's
/// failure never stops the job that walks the others. They only store raw rows (CLAUDE.md §1.1);
/// normalizing them is the separate background job (CLAUDE.md §1.3).
/// </summary>
public interface IIntegrationSyncService
{
    /// <summary>Lists the active integrations that have a polling schedule, oldest sync first.</summary>
    Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Incremental sync: asks the provider for what changed after the integration's cursor (less the
    /// configured overlap), stores it as one <c>IncrementalSync</c> batch, and only after those rows
    /// are committed moves the cursor and <c>LastSuccessfulSyncAt</c>. A failure leaves both as they
    /// were and flips the integration's health: Degraded, and Unhealthy once its circuit breaker opens.
    /// While the breaker is open the provider is not called.
    /// </summary>
    Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>
    /// Reconciliation: re-pulls a wider window (<see cref="AttendanceSyncOptions.ReconciliationWindowHours"/>
    /// back from now) than the cursor covers, as a <c>Reconciliation</c> batch. What the cursor already
    /// delivered arrives again and lands as duplicates at normalization. The cursor and
    /// <c>LastSuccessfulSyncAt</c> are not touched. A failure degrades health as for a sync; a success
    /// does not restore it, which stays the incremental sync's to say.
    /// </summary>
    Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the batches of successful syncs and reconciliations that delivered nothing and are
    /// older than <see cref="AttendanceSyncOptions.EmptySyncRetentionDays"/>. Failed batches, batches
    /// with raw rows and uploads are never deleted. Returns how many were removed.
    /// </summary>
    Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken);
}
