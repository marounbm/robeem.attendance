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
            .Where(i => i.Status == AttendanceIntegrationStatus.Active
                && i.Connections.Any(c => c.IsDefault && c.IsActive && c.PollingSchedule != null && c.PollingSchedule != string.Empty))
            .Select(i => new PollingIntegration(i.Id, i.Connections.Where(c => c.IsDefault).Select(c => c.PollingSchedule!).First(), i.LastSuccessfulSyncAt))
            .ToListAsync(cancellationToken);

        // Never-synced first. Done here: the position of NULLs in an ORDER BY differs between databases.
        return rows.OrderBy(r => r.LastSuccessfulSyncAt ?? DateTime.MinValue).ThenBy(r => r.IntegrationId).ToList();
    }

    public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) =>
        RunAsync(integrationId, Mode.Incremental, cancellationToken);

    public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) =>
        RunAsync(integrationId, Mode.Reconciliation, cancellationToken);

    public async Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken)
    {
        var days = _options.EmptySyncRetentionDays;
        if (days <= 0)
        {
            return 0;
        }

        // Only a successful sync or reconciliation that delivered nothing, with no raw row pointing
        // at it, and old enough. A failed batch (the evidence of an outage), a batch with rows and an
        // upload are never touched; raw rows are not involved (CLAUDE.md §1.2).
        var cutoff = _clock.GetLocalNow().DateTime.AddDays(-days);
        var stale = await _db.AttendanceProcessingBatches
            .Where(b => (b.BatchType == AttendanceBatchType.IncrementalSync || b.BatchType == AttendanceBatchType.Reconciliation)
                && b.Status == AttendanceBatchStatus.Succeeded
                && b.RecordsIngested == 0
                && b.StartedAt < cutoff
                && !_db.RawAttendanceTransactions.Any(r => r.ImportBatchId == b.Id))
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        _db.AttendanceProcessingBatches.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private static string Truncate(string text) => text.Length <= ErrorSummaryMaxLength ? text : text[..ErrorSummaryMaxLength];

    private async Task<SyncOutcome> RunAsync(int integrationId, Mode mode, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .Include(i => i.Connector).ThenInclude(c => c!.Vendor)
            .Include(i => i.Connections)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: "The integration does not exist.");
        }

        if (integration.Status != AttendanceIntegrationStatus.Active)
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: $"The integration is {integration.Status}.");
        }

        // The integration is pulled through its default connection. One that has none (never configured) is read with no secret and no settings.
        var connection = integration.Connections.FirstOrDefault(c => c.IsDefault);
        if (connection is { IsActive: false })
        {
            return new SyncOutcome(SyncOutcomeStatus.Skipped, Message: $"The connection '{connection.Name}' is switched off.");
        }

        // The vendor's key is what the raw rows record as their source; the connector's adapter key picks the code that fetches them.
        var vendorKey = integration.Connector!.Vendor!.VendorKey;
        var adapterKey = integration.Connector.AdapterKey;
        var handle = _resilience.Get(_db.CurrentTenantId, integration.Id);
        var batchType = mode == Mode.Incremental ? AttendanceBatchType.IncrementalSync : AttendanceBatchType.Reconciliation;

        // Adapters are keyed services, one per AttendanceConnectorDefinition.AdapterKey (CLAUDE.md §1.9).
        var adapter = adapterKey is { Length: > 0 } ? _services.GetKeyedService<IAttendanceAdapter>(adapterKey) : null;
        if (adapter is null)
        {
            return await FailAsync(integration.Id, connection?.Id, batchType, $"No adapter is registered for connector '{integration.Connector.ConnectorKey}' (adapter key '{adapterKey}').", handle);
        }

        var config = new AttendanceIntegrationConfig(integration.Id, vendorKey, connection?.SecretRef ?? string.Empty)
        {
            ConnectionId = connection?.Id,
            Settings = ConnectionSettingsValidator.Parse(connection?.Settings),
            SyncOverlap = TimeSpan.FromMinutes(Math.Max(0, _options.OverlapMinutes)),
        };
        var cursor = string.IsNullOrEmpty(integration.SyncCursor) ? null : new SyncCursor(integration.SyncCursor);
        var window = new DateRange(_clock.GetUtcNow() - TimeSpan.FromHours(_options.ReconciliationWindowHours), _clock.GetUtcNow());

        AttendanceBatchResult retrieved;
        try
        {
            // Retry and the breaker wrap the vendor call only: a storage failure says nothing about the vendor.
            retrieved = await handle.Pipeline.ExecuteAsync(
                async ct =>
                {
                    // The secret lives for this call only (CLAUDE.md §2.8), and a secret store that is down is retried like the vendor.
                    var call = config with { ConnectionSecret = await _secrets.GetSecretAsync(config.ConnectionConfigRef, ct) };
                    return mode == Mode.Incremental
                        ? await adapter.GetTransactionChangesAsync(call, cursor, ct)
                        : await adapter.RetrieveAttendanceTransactionsAsync(call, window, ct);
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
            return new SyncOutcome(SyncOutcomeStatus.CircuitOpen, Message: "The circuit breaker is open: the vendor has been failing.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await FailAsync(integration.Id, connection?.Id, batchType, ex.GetBaseException().Message, handle, ex);
        }

        try
        {
            // Every successful run leaves a batch, also one that delivered nothing, so the health
            // report's sync success rate counts the quiet syncs too. The empty ones are pruned later
            // (PruneEmptyBatchesAsync). The batch and its rows are committed together, and before
            // anything else changes.
            var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, ConnectionId = connection?.Id, BatchType = batchType, StartedAt = _clock.GetLocalNow().DateTime };
            _db.AttendanceProcessingBatches.Add(batch);
            if (retrieved.Transactions.Count > 0)
            {
                await _writer.AddTransactionsAsync(integration, batch, vendorKey, retrieved.Transactions, cancellationToken);
            }
            else
            {
                batch.Status = AttendanceBatchStatus.Succeeded;
                batch.CompletedAt = _clock.GetLocalNow().DateTime;
            }

            await _db.SaveChangesAsync(cancellationToken);
            int? batchId = batch.Id;
            var stored = batch.RecordsIngested;

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
            return await FailAsync(integration.Id, connection?.Id, batchType, ex.GetBaseException().Message, handle, ex);
        }
    }

    /// <summary>Records a failed run: a Failed batch with the reason, and the integration's health. The cursor is not touched.</summary>
    private async Task<SyncOutcome> FailAsync(int integrationId, int? connectionId, AttendanceBatchType batchType, string message, IntegrationPipeline handle, Exception? exception = null)
    {
        _logger.LogWarning(exception, "Integration {IntegrationId}: {BatchType} failed: {Message}", integrationId, batchType, message);

        // Drop whatever failed to save, and record the failure on freshly loaded state.
        _db.ChangeTracker.Clear();
        var integration = await _db.AttendanceIntegrations.SingleAsync(i => i.Id == integrationId, CancellationToken.None);
        var batch = new AttendanceProcessingBatch
        {
            IntegrationId = integrationId,
            ConnectionId = connectionId,
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
