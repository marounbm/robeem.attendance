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
    private const int MaxDaysPerRun = 200;

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

    public string DisplayName => "Normalization and day processing";

    public string Description => "Turns the raw punches that were uploaded or pulled into normalized punches, then calculates the attendance of the days they belong to.";

    public bool Enabled => _options.Enabled;

    public JobSchedule Schedule => JobSchedule.Every(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)));

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
                // One tenant's failure (database down, missing interpreter) must not stop the others;
                // its rows stay Pending and are retried on the next run.
                _logger.LogError(ex, "Attendance normalization failed for tenant {TenantId}.", tenant.Id);
            }
        }
    }

    public async Task<JobRunResult> RunForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        await using var scope = _tenantScopes.CreateScope(tenantId);
        var normalization = scope.ServiceProvider.GetRequiredService<INormalizationService>();

        int normalized = 0, failed = 0;
        foreach (var batchId in await normalization.ListBatchesWithPendingTransactionsAsync(cancellationToken))
        {
            var result = await normalization.NormalizeBatchAsync(batchId, cancellationToken);
            normalized += result.Normalized;
            failed += result.Failed;
            _logger.LogInformation(
                "Normalized batch {BatchId} of tenant {TenantId}: {Normalized} normalized ({Duplicates} duplicates, {Flagged} flagged), {Failed} failed.",
                batchId,
                tenantId,
                result.Normalized,
                result.Duplicates,
                result.Flagged,
                result.Failed);
        }

        // Then the days those punches belong to, so the Attendance screen fills in without a manual reprocess.
        var days = await scope.ServiceProvider.GetRequiredService<IPendingDayProcessor>().ProcessPendingAsync(MaxDaysPerRun, cancellationToken);
        if (days > 0)
        {
            _logger.LogInformation("Processed {Days} attendance day(s) of tenant {TenantId}.", days, tenantId);
        }

        var work = normalized + failed + days;
        return work == 0
            ? JobRunResult.Idle
            : new JobRunResult(work, $"{normalized} row(s) normalized, {failed} unusable; {days} day(s) calculated.");
    }
}
