using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance;

/// <summary>
/// Normalizes ingested raw transactions in the background, so ingestion never does it inline
/// (CLAUDE.md §1.3). It polls every tenant in turn, each in its own tenant scope (CLAUDE.md §2.8).
/// </summary>
/// <remarks>
/// A minimal stand-in until P10 chooses the scheduler; that choice replaces this loop, not
/// <see cref="INormalizationService"/>. Run a single Api instance until then: two instances would
/// normalize the same raw rows twice.
/// </remarks>
public sealed class AttendanceNormalizationHostedService : BackgroundService
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly IOptions<AttendanceNormalizationOptions> _options;
    private readonly ILogger<AttendanceNormalizationHostedService> _logger;

    public AttendanceNormalizationHostedService(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceNormalizationOptions> options,
        ILogger<AttendanceNormalizationHostedService> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Attendance normalization is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds)));
        do
        {
            foreach (var tenant in _tenants.Tenants)
            {
                await NormalizeTenantAsync(tenant.Id, stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task NormalizeTenantAsync(string tenantId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _tenantScopes.CreateScope(tenantId);
            var normalization = scope.ServiceProvider.GetRequiredService<INormalizationService>();

            foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(stoppingToken))
            {
                var result = await normalization.NormalizeBatchAsync(batchId, stoppingToken);
                _logger.LogInformation(
                    "Normalized batch {BatchId} of tenant {TenantId}: {Normalized} normalized ({Duplicates} duplicates, {Flagged} flagged), {Failed} failed.",
                    batchId,
                    tenantId,
                    result.Normalized,
                    result.Duplicates,
                    result.Flagged,
                    result.Failed);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // One tenant's failure (database down, missing interpreter) must not stop the others;
            // its rows stay Pending and are retried on the next poll.
            _logger.LogError(ex, "Attendance normalization failed for tenant {TenantId}.", tenantId);
        }
    }
}
