using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Jobs;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// The nightly reconciliation (study §12.4): re-pulls a wider window than the incremental cursor
/// covers, for every active polling integration that has synced before. Whatever the cursor
/// already delivered arrives again and is absorbed by duplicate detection (CLAUDE.md §1.4).
/// </summary>
public sealed class ReconciliationJob : IScheduledJob
{
    private const int HistoryRetentionDays = 30;

    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceSyncOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceSyncOptions> options,
        TimeProvider clock,
        ILogger<ReconciliationJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "attendance-reconciliation";

    public string DisplayName => "Nightly reconciliation";

    public string Description => "Re-pulls a wider window than the incremental cursor from every integration that has synced before, then removes old empty sync batches and old job history.";

    public bool Enabled => _options.Enabled && _options.ReconciliationEnabled;

    public JobSchedule Schedule => JobSchedule.OnCron(_options.ReconciliationCron);

    /// <summary>Runs for every tenant, one after the other; one tenant's failure is logged and does not stop the others.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                await RunForTenantAsync(tenant.Id, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Attendance reconciliation failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = _tenantScopes.CreateScope(tenantId);
        var sync = scope.ServiceProvider.GetRequiredService<IIntegrationSyncService>();

        // An integration that never synced has nothing to reconcile, and may not support a
        // pull at all (a manual-upload one).
        int reconciled = 0, records = 0;
        var problems = new List<string>();
        foreach (var integration in (await sync.ListPollingIntegrationsAsync(cancellationToken)).Where(i => i.LastSuccessfulSyncAt is not null))
        {
            try
            {
                var outcome = await sync.ReconcileAsync(integration.IntegrationId, cancellationToken);
                _logger.LogInformation(
                    "Reconciliation of integration {IntegrationId} of tenant {TenantId}: {Status}, {Records} records. {Message}",
                    integration.IntegrationId,
                    tenantId,
                    outcome.Status,
                    outcome.RecordsIngested,
                    outcome.Message);

                if (outcome.Status == SyncOutcomeStatus.Skipped)
                {
                    continue;
                }

                reconciled++;
                records += outcome.RecordsIngested;
                if (outcome.Status != SyncOutcomeStatus.Succeeded)
                {
                    problems.Add($"Integration {integration.IntegrationId}: {outcome.Status}{(string.IsNullOrWhiteSpace(outcome.Message) ? string.Empty : " - " + outcome.Message)}");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Reconciliation of integration {IntegrationId} of tenant {TenantId} threw.", integration.IntegrationId, tenantId);
                reconciled++;
                problems.Add($"Integration {integration.IntegrationId}: {ex.GetBaseException().Message}");
            }
        }

        // Nightly housekeeping: the batches of quiet syncs only live for the health window.
        var pruned = await sync.PruneEmptyBatchesAsync(cancellationToken);
        if (pruned > 0)
        {
            _logger.LogInformation("Pruned {Count} empty sync batches of tenant {TenantId}.", pruned, tenantId);
        }

        // And the job history, which only matters for a while.
        var history = await scope.ServiceProvider.GetRequiredService<IJobRunLog>().PruneAsync(_clock.GetLocalNow().DateTime.AddDays(-HistoryRetentionDays), cancellationToken);

        var summary = $"{reconciled} integration(s) reconciled, {records} record(s) re-pulled; {pruned} empty batch(es) and {history} old job run(s) removed."
            + (problems.Count == 0 ? string.Empty : " Problems: " + string.Join("; ", problems));
        return new JobRunResult(reconciled + pruned, summary, problems.Count > 0);
    }
}
