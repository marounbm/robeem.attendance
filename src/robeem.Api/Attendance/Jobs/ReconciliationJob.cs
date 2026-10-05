using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// The nightly reconciliation (study §12.4): re-pulls a wider window than the incremental cursor
/// covers, for every active polling integration that has synced before. Whatever the cursor
/// already delivered arrives again and is absorbed by duplicate detection (CLAUDE.md §1.4).
/// </summary>
public sealed class ReconciliationJob : IScheduledJob
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceSyncOptions _options;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceSyncOptions> options,
        ILogger<ReconciliationJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "attendance-reconciliation";

    public bool Enabled => _options.Enabled && _options.ReconciliationEnabled;

    public JobSchedule Schedule => JobSchedule.OnCron(_options.ReconciliationCron);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                await using var scope = _tenantScopes.CreateScope(tenant.Id);
                var sync = scope.ServiceProvider.GetRequiredService<IIntegrationSyncService>();

                // An integration that never synced has nothing to reconcile, and may not support a
                // pull at all (a manual-upload one).
                foreach (var integration in (await sync.ListPollingIntegrationsAsync(cancellationToken)).Where(i => i.LastSuccessfulSyncAt is not null))
                {
                    try
                    {
                        var outcome = await sync.ReconcileAsync(integration.IntegrationId, cancellationToken);
                        _logger.LogInformation(
                            "Reconciliation of integration {IntegrationId} of tenant {TenantId}: {Status}, {Records} records. {Message}",
                            integration.IntegrationId,
                            tenant.Id,
                            outcome.Status,
                            outcome.RecordsIngested,
                            outcome.Message);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Reconciliation of integration {IntegrationId} of tenant {TenantId} threw.", integration.IntegrationId, tenant.Id);
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Attendance reconciliation failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }
}
