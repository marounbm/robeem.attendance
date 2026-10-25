using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// Incremental sync of every active integration that has a polling schedule, in every tenant
/// (study §12.4). The job ticks often; an integration is synced when its own
/// <c>PollingScheduleCron</c> says it is due. A failure, retries and the circuit breaker are the
/// sync service's business, so one integration failing never stops the others.
/// </summary>
public sealed class IntegrationSyncJob : IScheduledJob
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceSyncOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<IntegrationSyncJob> _logger;

    // When each integration was last tried: a failed sync is not retried before the next cron time.
    private readonly ConcurrentDictionary<(string TenantId, int IntegrationId), DateTimeOffset> _lastAttempt = new();
    private readonly ConcurrentDictionary<(string TenantId, int IntegrationId), bool> _warnedAboutCron = new();

    public IntegrationSyncJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceSyncOptions> options,
        TimeProvider clock,
        ILogger<IntegrationSyncJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public string Name => "attendance-sync";

    public string DisplayName => "Integration sync";

    public string Description => "Pulls new punches from every active integration that reads from a device or a server, each when its own polling schedule says it is due.";

    public bool Enabled => _options.Enabled;

    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromSeconds(Math.Max(1, _options.TickSeconds)));

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
                _logger.LogError(ex, "Attendance sync failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = _tenantScopes.CreateScope(tenantId);
        var sync = scope.ServiceProvider.GetRequiredService<IIntegrationSyncService>();

        int synced = 0, records = 0;
        var problems = new List<string>();
        foreach (var integration in await sync.ListPollingIntegrationsAsync(cancellationToken))
        {
            if (!IsDue(tenantId, integration))
            {
                continue;
            }

            _lastAttempt[(tenantId, integration.IntegrationId)] = _clock.GetLocalNow();
            try
            {
                var outcome = await sync.SyncAsync(integration.IntegrationId, cancellationToken);
                _logger.LogInformation(
                    "Sync of integration {IntegrationId} of tenant {TenantId}: {Status}, {Records} records. {Message}",
                    integration.IntegrationId,
                    tenantId,
                    outcome.Status,
                    outcome.RecordsIngested,
                    outcome.Message);

                if (outcome.Status == SyncOutcomeStatus.Skipped)
                {
                    continue;
                }

                synced++;
                records += outcome.RecordsIngested;
                if (outcome.Status != SyncOutcomeStatus.Succeeded)
                {
                    problems.Add($"Integration {integration.IntegrationId}: {outcome.Status}{(string.IsNullOrWhiteSpace(outcome.Message) ? string.Empty : " - " + outcome.Message)}");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // The service turns a vendor failure into an outcome; this is something worse (the
                // database, say). It must not keep this tenant's other integrations from syncing.
                _logger.LogError(ex, "Sync of integration {IntegrationId} of tenant {TenantId} threw.", integration.IntegrationId, tenantId);
                synced++;
                problems.Add($"Integration {integration.IntegrationId}: {ex.GetBaseException().Message}");
            }
        }

        return synced == 0
            ? JobRunResult.Idle
            : new JobRunResult(synced, $"{synced} integration(s) synced, {records} record(s) stored." + (problems.Count == 0 ? string.Empty : " Problems: " + string.Join("; ", problems)), problems.Count > 0);
    }

    private bool IsDue(string tenantId, PollingIntegration integration)
    {
        if (!CronSchedule.TryParse(integration.Cron, out _))
        {
            if (_warnedAboutCron.TryAdd((tenantId, integration.IntegrationId), true))
            {
                _logger.LogWarning("Integration {IntegrationId} of tenant {TenantId} has an invalid polling schedule '{Cron}'; it is not synced.", integration.IntegrationId, tenantId, integration.Cron);
            }

            return false;
        }

        // The last try, else the last success (it survives a restart); none at all means due at once.
        DateTimeOffset? reference = _lastAttempt.TryGetValue((tenantId, integration.IntegrationId), out var attempt)
            ? attempt
            : integration.LastSuccessfulSyncAt is { } synced ? ToOffset(synced) : null;
        return CronSchedule.IsDue(integration.Cron, reference, _clock.GetLocalNow(), _clock.LocalTimeZone);
    }

    private DateTimeOffset ToOffset(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _clock.LocalTimeZone.GetUtcOffset(local));
}
