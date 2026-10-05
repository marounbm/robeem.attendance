using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class IngestionService : IIngestionService
{
    private readonly IRobeemDbContext _db;
    private readonly RawBatchWriter _writer;
    private readonly IServiceProvider _services;

    public IngestionService(IRobeemDbContext db, IRawPayloadSanitizer sanitizer, IServiceProvider services)
    {
        _db = db;
        _writer = new RawBatchWriter(db, sanitizer);
        _services = services;
    }

    public async Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .Include(i => i.Provider)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return null;
        }

        // The batch is saved first, so a run that dies half-way is still visible.
        var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = AttendanceBatchType.ManualUpload };
        _db.AttendanceProcessingBatches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        var providerKey = integration.Provider!.ProviderKey;
        try
        {
            // Adapters are keyed services, one per AttendanceProvider.ProviderKey (CLAUDE.md §1.9).
            var provider = _services.GetKeyedService<IAttendanceProvider>(providerKey)
                ?? throw new InvalidOperationException($"No adapter is registered for provider '{providerKey}'.");
            var config = new AttendanceIntegrationConfig(integration.Id, providerKey, integration.ConnectionConfigRef) { UploadFilePath = filePath };

            var retrieved = await provider.RetrieveAttendanceTransactionsAsync(config, null, cancellationToken);
            await _writer.AddTransactionsAsync(integration, batch, providerKey, retrieved.Transactions, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResult(batch);
        }
        catch (Exception ex)
        {
            // The adapter or the database failed as a whole (no row was stored): record why on the
            // batch. Cancellation is recorded too, then rethrown.
            var failed = await MarkFailedAsync(batch.Id, ex is OperationCanceledException ? "Cancelled." : ex.GetBaseException().Message);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return ToResult(failed);
        }
    }

    private static AttendanceIngestionResult ToResult(AttendanceProcessingBatch batch) => new()
    {
        BatchId = batch.Id,
        Status = batch.Status.ToString(),
        RecordsIngested = batch.RecordsIngested,
        RecordsFailed = batch.RecordsFailed,
        ErrorSummary = batch.ErrorSummary,
    };

    private async Task<AttendanceProcessingBatch> MarkFailedAsync(int batchId, string error)
    {
        // Drop whatever failed to save, then record the failure on a freshly loaded batch.
        _db.ChangeTracker.Clear();
        var batch = await _db.AttendanceProcessingBatches.SingleAsync(b => b.Id == batchId, CancellationToken.None);
        batch.Status = AttendanceBatchStatus.Failed;
        batch.RecordsIngested = 0;
        batch.RecordsFailed = 0;
        batch.ErrorSummary = error;
        batch.CompletedAt = DateTime.Now;
        await _db.SaveChangesAsync(CancellationToken.None);
        return batch;
    }
}
