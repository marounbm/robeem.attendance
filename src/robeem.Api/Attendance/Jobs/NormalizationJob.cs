using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Tenancy;

namespace robeem.Api.Attendance.Jobs;

/// <summary>
/// Normalizes ingested raw transactions in the background, so ingestion never does it inline
/// (CLAUDE.md §1.3). Rows that cannot be normalized yet stay Pending and are picked up again on the
/// next run.
/// </summary>
public sealed class NormalizationJob : IScheduledJob
{
    private readonly ITenantRegistry _tenants;
    private readonly ITenantScopeFactory _tenantScopes;
    private readonly AttendanceNormalizationOptions _options;
    private readonly ILogger<NormalizationJob> _logger;

    public NormalizationJob(
        ITenantRegistry tenants,
        ITenantScopeFactory tenantScopes,
        IOptions<AttendanceNormalizationOptions> options,
        ILogger<NormalizationJob> logger)
    {
        _tenants = tenants;
        _tenantScopes = tenantScopes;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "attendance-normalization";

    public bool Enabled => _options.Enabled;

    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in _tenants.Tenants)
        {
            try
            {
                await using var scope = _tenantScopes.CreateScope(tenant.Id);
                var normalization = scope.ServiceProvider.GetRequiredService<INormalizationService>();

                foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(cancellationToken))
                {
                    var result = await normalization.NormalizeBatchAsync(batchId, cancellationToken);
                    _logger.LogInformation(
                        "Normalized batch {BatchId} of tenant {TenantId}: {Normalized} normalized ({Duplicates} duplicates, {Flagged} flagged), {Failed} failed.",
                        batchId,
                        tenant.Id,
                        result.Normalized,
                        result.Duplicates,
                        result.Flagged,
                        result.Failed);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // One tenant's failure (database down, missing interpreter) must not stop the others;
                // its rows stay Pending and are retried on the next run.
                _logger.LogError(ex, "Attendance normalization failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }
}
