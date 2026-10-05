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

    public bool Enabled => _options.Enabled;

    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromSeconds(Math.Max(1, _options.TickSeconds)));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                await using var scope = _tenantScopes.CreateScope(tenant.Id);
                var sync = scope.ServiceProvider.GetRequiredService<IIntegrationSyncService>();

                foreach (var integration in await sync.ListPollingIntegrationsAsync(cancellationToken))
                {
                    if (!IsDue(tenant.Id, integration))
                    {
                        continue;
                    }

                    _lastAttempt[(tenant.Id, integration.IntegrationId)] = _clock.GetLocalNow();
                    var outcome = await sync.SyncAsync(integration.IntegrationId, cancellationToken);
                    _logger.LogInformation(
                        "Sync of integration {IntegrationId} of tenant {TenantId}: {Status}, {Records} records. {Message}",
                        integration.IntegrationId,
                        tenant.Id,
                        outcome.Status,
                        outcome.RecordsIngested,
                        outcome.Message);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Attendance sync failed for tenant {TenantId}.", tenant.Id);
            }
        }
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
