using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class IngestionService : IIngestionService
{
    private readonly IRobeemDbContext _db;
    private readonly IRawPayloadSanitizer _sanitizer;
    private readonly IServiceProvider _services;

    public IngestionService(IRobeemDbContext db, IRawPayloadSanitizer sanitizer, IServiceProvider services)
    {
        _db = db;
        _sanitizer = sanitizer;
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
            var timeZones = await LoadDeviceTimeZonesAsync(integration.Id, cancellationToken);

            foreach (var transaction in retrieved.Transactions)
            {
                var raw = ToRawTransaction(transaction, integration.Id, batch.Id, providerKey, timeZones);
                _db.RawAttendanceTransactions.Add(raw);
                batch.RecordsIngested++;
                if (raw.ProcessingStatus == RawTransactionProcessingStatus.Failed)
                {
                    batch.RecordsFailed++;
                }
            }

            batch.Status = batch.RecordsFailed == 0 ? AttendanceBatchStatus.Succeeded
                : batch.RecordsFailed == batch.RecordsIngested ? AttendanceBatchStatus.Failed
                : AttendanceBatchStatus.PartiallyFailed;
            batch.CompletedAt = DateTime.Now;
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

    private static string Hash(int integrationId, string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{integrationId}\n{payload}"));
        return Convert.ToHexStringLower(bytes);
    }

    private static AttendanceIngestionResult ToResult(AttendanceProcessingBatch batch) => new()
    {
        BatchId = batch.Id,
        Status = batch.Status.ToString(),
        RecordsIngested = batch.RecordsIngested,
        RecordsFailed = batch.RecordsFailed,
        ErrorSummary = batch.ErrorSummary,
    };

    private RawAttendanceTransaction ToRawTransaction(
        ExternalAttendanceTransaction transaction,
        int integrationId,
        int batchId,
        string providerKey,
        IReadOnlyDictionary<string, (int Id, string TimeZoneId)> devices)
    {
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = integrationId,
            ImportBatchId = batchId,
            SourceSystem = providerKey,
            DeviceExternalId = transaction.DeviceExternalId,
            ExternalTransactionId = transaction.ExternalTransactionId,
            ExternalEmployeeId = transaction.ExternalEmployeeId,
            TransactionTimestampRaw = transaction.TransactionTimestampRaw,
            TransactionTypeRaw = transaction.TransactionTypeRaw,
            DirectionRaw = transaction.DirectionRaw,
        };

        string? timeZoneId = null;
        if (transaction.DeviceExternalId is { } deviceExternalId && devices.TryGetValue(deviceExternalId, out var device))
        {
            raw.DeviceId = device.Id;
            timeZoneId = device.TimeZoneId;
        }

        // Biometric fields never reach the database (CLAUDE.md §1.10). A payload that cannot be
        // checked is not stored at all, but the row still is (CLAUDE.md §1.1).
        try
        {
            raw.RawPayload = _sanitizer.Sanitize(transaction.RawPayload);
        }
        catch (FormatException ex)
        {
            raw.RawPayload = "{}";
            raw.ProcessingStatus = RawTransactionProcessingStatus.Failed;
            raw.ProcessingError = ex.Message;
        }

        raw.RecordHash = Hash(integrationId, raw.RawPayload);

        if (transaction.Timestamp is { } timestamp)
        {
            raw.TransactionTimestampParsed = timestamp.ToUtc(timeZoneId);
        }
        else if (raw.ProcessingStatus != RawTransactionProcessingStatus.Failed)
        {
            raw.ProcessingStatus = RawTransactionProcessingStatus.Failed;
            raw.ProcessingError = $"Unparseable timestamp '{transaction.TransactionTimestampRaw}'.";
        }

        return raw;
    }

    private async Task<IReadOnlyDictionary<string, (int Id, string TimeZoneId)>> LoadDeviceTimeZonesAsync(int integrationId, CancellationToken cancellationToken)
    {
        var devices = await _db.AttendanceDevices
            .Where(d => d.IntegrationId == integrationId)
            .Select(d => new { d.ExternalDeviceId, d.Id, d.TimeZoneId })
            .ToListAsync(cancellationToken);

        return devices.ToDictionary(d => d.ExternalDeviceId, d => (d.Id, d.TimeZoneId), StringComparer.Ordinal);
    }

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
