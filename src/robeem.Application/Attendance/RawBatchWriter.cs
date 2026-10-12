using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Stages a provider's transactions as <see cref="RawAttendanceTransaction"/>s of a batch: the
/// storage step shared by manual upload and scheduled sync. Every transaction is stored, a
/// malformed one marked Failed (CLAUDE.md §1.1), and biometric fields never reach the database
/// (CLAUDE.md §1.10). It saves nothing: the caller decides when the rows are committed.
/// </summary>
internal sealed class RawBatchWriter
{
    private const int ExampleMaxLength = 300;

    private readonly IRobeemDbContext _db;
    private readonly IRawPayloadSanitizer _sanitizer;

    public RawBatchWriter(IRobeemDbContext db, IRawPayloadSanitizer sanitizer)
    {
        _db = db;
        _sanitizer = sanitizer;
    }

    /// <summary>Adds a raw row per transaction, and sets the batch's counts, status and completion time.</summary>
    public async Task AddTransactionsAsync(
        AttendanceIntegration integration,
        AttendanceProcessingBatch batch,
        string providerKey,
        IReadOnlyList<ExternalAttendanceTransaction> transactions,
        CancellationToken cancellationToken)
    {
        var timeZones = await LoadDeviceTimeZonesAsync(integration.Id, cancellationToken);

        // An uploaded file has no physical device: the integration's own device takes every punch of it.
        (int Id, string TimeZoneId)? defaultDevice = integration.Transport == AttendanceTransport.FileUpload
            && integration.DefaultDeviceId is { } defaultDeviceId
            && timeZones.Values.FirstOrDefault(d => d.Id == defaultDeviceId) is { Id: > 0 } found
                ? found
                : null;

        string? firstError = null;
        foreach (var transaction in transactions)
        {
            var raw = ToRawTransaction(transaction, integration.Id, batch, providerKey, timeZones, defaultDevice);
            _db.RawAttendanceTransactions.Add(raw);
            batch.RecordsIngested++;
            if (raw.ProcessingStatus == RawTransactionProcessingStatus.Failed)
            {
                batch.RecordsFailed++;
                firstError ??= raw.ProcessingError;
            }
        }

        // Say why, with one example: a count of unusable rows alone does not tell the reader what to fix.
        if (firstError is not null)
        {
            var example = firstError.Length <= ExampleMaxLength ? firstError : firstError[..ExampleMaxLength];
            batch.ErrorSummary = $"For example: {example}";
        }

        batch.Status = batch.RecordsFailed == 0 ? AttendanceBatchStatus.Succeeded
            : batch.RecordsFailed == batch.RecordsIngested ? AttendanceBatchStatus.Failed
            : AttendanceBatchStatus.PartiallyFailed;
        batch.CompletedAt = DateTime.Now;
    }

    private static string Hash(int integrationId, string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{integrationId}\n{payload}"));
        return Convert.ToHexStringLower(bytes);
    }

    private RawAttendanceTransaction ToRawTransaction(
        ExternalAttendanceTransaction transaction,
        int integrationId,
        AttendanceProcessingBatch batch,
        string providerKey,
        IReadOnlyDictionary<string, (int Id, string TimeZoneId)> devices,
        (int Id, string TimeZoneId)? defaultDevice)
    {
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = integrationId,
            ImportBatch = batch,
            SourceSystem = providerKey,
            DeviceExternalId = transaction.DeviceExternalId,
            ExternalTransactionId = transaction.ExternalTransactionId,
            ExternalEmployeeId = transaction.ExternalEmployeeId,
            TransactionTimestampRaw = transaction.TransactionTimestampRaw,
            TransactionTypeRaw = transaction.TransactionTypeRaw,
            DirectionRaw = transaction.DirectionRaw,
        };

        string? timeZoneId = null;
        if (defaultDevice is { } uploadDevice)
        {
            // DeviceExternalId stays as the file said; DeviceId is who the punch is attributed to.
            raw.DeviceId = uploadDevice.Id;
            timeZoneId = uploadDevice.TimeZoneId;
        }
        else if (transaction.DeviceExternalId is { } deviceExternalId && devices.TryGetValue(deviceExternalId, out var device))
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
}
