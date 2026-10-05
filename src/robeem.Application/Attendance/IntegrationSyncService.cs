using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class IntegrationSyncService : IIntegrationSyncService
{
    private const int ErrorSummaryMaxLength = 1000;

    private readonly IRobeemDbContext _db;
    private readonly RawBatchWriter _writer;
    private readonly IServiceProvider _services;
    private readonly IntegrationResilience _resilience;
    private readonly AttendanceSyncOptions _options;
    private readonly ISecretStore _secrets;
    private readonly TimeProvider _clock;
    private readonly ILogger<IntegrationSyncService> _logger;

    public IntegrationSyncService(
        IRobeemDbContext db,
        IRawPayloadSanitizer sanitizer,
        IServiceProvider services,
        IntegrationResilience resilience,
        IOptions<AttendanceSyncOptions> options,
        ISecretStore secrets,
        TimeProvider clock,
        ILogger<IntegrationSyncService> logger)
    {
        _db = db;
        _writer = new RawBatchWriter(db, sanitizer);
        _services = services;
        _resilience = resilience;
        _secrets = secrets;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    private enum Mode
    {
        Incremental,
        Reconciliation,
    }

    public async Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.AttendanceIntegrations
            .AsNoTracking()
            .Where(i => i.Status == AttendanceIntegrationStatus.Active && i.PollingScheduleCron != null && i.PollingScheduleCron != string.Empty)
            .Select(i => new PollingIntegration(i.Id, i.PollingScheduleCron!, i.LastSuccessfulSyncAt))
            .ToListAsync(cancellationToken);

        // Never-synced first. Done here: the position of NULLs in an ORDER BY differs between databases.
        return rows.OrderBy(r => r.LastSuccessfulSyncAt ?? DateTime.MinValue).ThenBy(r => r.IntegrationId).ToList();
    }

    public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) =>
        RunAsync(integrationId, Mode.Incremental, cancellationToken);

    public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) =>
        RunAsync(integrationId, Mode.Reconciliation, cancellationToken);

    private static string Truncate(string text) => text.Length <= ErrorSummaryMaxLength ? text : text[..ErrorSummaryMaxLength];

    private async Task<SyncOutcome> RunAsync(int integrationId, Mode mode, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .Include(i => i.Provider)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: "The integration does not exist.");
        }

        if (integration.Status != AttendanceIntegrationStatus.Active)
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: $"The integration is {integration.Status}.");
        }

        var providerKey = integration.Provider!.ProviderKey;
        var handle = _resilience.Get(_db.CurrentTenantId, integration.Id);
        var batchType = mode == Mode.Incremental ? AttendanceBatchType.IncrementalSync : AttendanceBatchType.Reconciliation;

        // Adapters are keyed services, one per AttendanceProvider.ProviderKey (CLAUDE.md §1.9).
        var provider = _services.GetKeyedService<IAttendanceProvider>(providerKey);
        if (provider is null)
        {
            return await FailAsync(integration.Id, batchType, $"No adapter is registered for provider '{providerKey}'.", handle);
        }

        var config = new AttendanceIntegrationConfig(integration.Id, providerKey, integration.ConnectionConfigRef)
        {
            SyncOverlap = TimeSpan.FromMinutes(Math.Max(0, _options.OverlapMinutes)),
        };
        var cursor = string.IsNullOrEmpty(integration.SyncCursor) ? null : new SyncCursor(integration.SyncCursor);
        var window = new DateRange(_clock.GetUtcNow() - TimeSpan.FromHours(_options.ReconciliationWindowHours), _clock.GetUtcNow());

        AttendanceBatchResult retrieved;
        try
        {
            // Retry and the breaker wrap the provider call only: a storage failure says nothing about the vendor.
            retrieved = await handle.Pipeline.ExecuteAsync(
                async ct =>
                {
                    // The secret lives for this call only (CLAUDE.md §2.8), and a secret store that is down is retried like the vendor.
                    var call = config with { ConnectionSecret = await _secrets.GetSecretAsync(config.ConnectionConfigRef, ct) };
                    return mode == Mode.Incremental
                        ? await provider.GetTransactionChangesAsync(call, cursor, ct)
                        : await provider.RetrieveAttendanceTransactionsAsync(call, window, ct);
                },
                cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: ex.Message);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Integration {IntegrationId}: the circuit breaker is open, {Mode} skipped.", integration.Id, mode);
            await EnsureUnhealthyAsync(integration);
            return new SyncOutcome(SyncOutcomeStatus.CircuitOpen, Message: "The circuit breaker is open: the provider has been failing.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FailAsync(integration.Id, batchType, ex.GetBaseException().Message, handle, ex);
        }

        try
        {
            int? batchId = null;
            var stored = 0;
            if (retrieved.Transactions.Count > 0)
            {
                // The batch and its rows are committed together, and before anything else changes.
                var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = batchType, StartedAt = _clock.GetLocalNow().DateTime };
                _db.AttendanceProcessingBatches.Add(batch);
                await _writer.AddTransactionsAsync(integration, batch, providerKey, retrieved.Transactions, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                (batchId, stored) = (batch.Id, batch.RecordsIngested);
            }

            if (mode == Mode.Incremental)
            {
                // Only now, with the rows durable, does the cursor move (study §12.4): if this save
                // fails, the next sync reads the same window again and duplicates absorb it.
                if (retrieved.NextCursor is { } next)
                {
                    integration.SyncCursor = next.Value;
                }

                integration.LastSuccessfulSyncAt = _clock.GetLocalNow().DateTime;
                integration.HealthStatus = AttendanceIntegrationHealth.Healthy;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new SyncOutcome(SyncOutcomeStatus.Succeeded, batchId, stored);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FailAsync(integration.Id, batchType, ex.GetBaseException().Message, handle, ex);
        }
    }

    /// <summary>Records a failed run: a Failed batch with the reason, and the integration's health. The cursor is not touched.</summary>
    private async Task<SyncOutcome> FailAsync(int integrationId, AttendanceBatchType batchType, string message, IntegrationPipeline handle, Exception? exception = null)
    {
        _logger.LogWarning(exception, "Integration {IntegrationId}: {BatchType} failed: {Message}", integrationId, batchType, message);

        // Drop whatever failed to save, and record the failure on freshly loaded state.
        _db.ChangeTracker.Clear();
        var integration = await _db.AttendanceIntegrations.SingleAsync(i => i.Id == integrationId, CancellationToken.None);
        var batch = new AttendanceProcessingBatch
        {
            IntegrationId = integrationId,
            BatchType = batchType,
            StartedAt = _clock.GetLocalNow().DateTime,
            Status = AttendanceBatchStatus.Failed,
            ErrorSummary = Truncate(message),
            CompletedAt = _clock.GetLocalNow().DateTime,
        };
        _db.AttendanceProcessingBatches.Add(batch);

        // Degraded while the breaker is closed; Unhealthy once it has opened, and it stays so until a sync succeeds.
        integration.HealthStatus = handle.IsOpen || integration.HealthStatus == AttendanceIntegrationHealth.Unhealthy
            ? AttendanceIntegrationHealth.Unhealthy
            : AttendanceIntegrationHealth.Degraded;
        await _db.SaveChangesAsync(CancellationToken.None);

        return new SyncOutcome(SyncOutcomeStatus.Failed, batch.Id, 0, message);
    }

    private async Task EnsureUnhealthyAsync(AttendanceIntegration integration)
    {
        if (integration.HealthStatus != AttendanceIntegrationHealth.Unhealthy)
        {
            integration.HealthStatus = AttendanceIntegrationHealth.Unhealthy;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
