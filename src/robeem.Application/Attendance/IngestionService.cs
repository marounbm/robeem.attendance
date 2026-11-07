using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class IngestionService : IIngestionService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly ICatalogReader _catalog;
    private readonly RawBatchWriter _writer;
    private readonly IServiceProvider _services;

    public IngestionService(IRobeemDbContext db, ICatalogReader catalog, IRawPayloadSanitizer sanitizer, IServiceProvider services)
    {
        _db = db;
        _catalog = catalog;
        _writer = new RawBatchWriter(db, sanitizer);
        _services = services;
    }

    public async Task<IReadOnlyList<UploadTarget>> ListUploadTargetsAsync(CancellationToken cancellationToken)
    {
        // Any connector may take manual uploads: the integration's default connection decides. An integration with no connection yet takes them.
        var integrations = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Status == AttendanceIntegrationStatus.Active
                && !i.Connections.Any(c => c.IsDefault && (c.Transport != AttendanceTransport.FileUpload || !c.IsActive)))
            .OrderBy(i => i.DisplayName)
            .Select(i => new { i.Id, i.DisplayName, i.ConnectorKey })
            .ToListAsync(cancellationToken);

        // The connectors and formats are the prime tenant's catalog. An integration whose connector, or whose vendor, is switched off is not a target.
        var connectors = await _catalog.GetConnectorsAsync(integrations.Select(i => i.ConnectorKey), cancellationToken);
        var targets = integrations
            .Where(i => connectors.TryGetValue(i.ConnectorKey, out var connector) && connector.IsActive && connector.VendorIsActive)
            .Select(i => new { Integration = i, Connector = connectors[i.ConnectorKey] })
            .ToList();

        var formatNames = targets
            .Select(t => PayloadFormatResolution.EffectiveFormatName(AttendanceTransport.FileUpload, t.Connector.DefaultPayloadFormatName))
            .OfType<string>()
            .ToList();
        var formats = await _catalog.GetLatestFormatsAsync(formatNames, cancellationToken);

        return targets
            .Select(t =>
            {
                var layout = PayloadFormatResolution.EffectiveFormatName(AttendanceTransport.FileUpload, t.Connector.DefaultPayloadFormatName) is { } formatName && formats.TryGetValue(formatName, out var format)
                    ? PayloadFormatLayout.Describe(format, t.Connector.VendorKey)
                    : NoFormatLayout(t.Connector.VendorKey, t.Connector.DisplayName);
                return new UploadTarget
                {
                    IntegrationId = t.Integration.Id,
                    DisplayName = t.Integration.DisplayName,
                    VendorKey = t.Connector.VendorKey,
                    VendorName = t.Connector.VendorName,
                    FileExtensions = layout.FileExtensions,
                    Layout = layout,
                };
            })
            .ToList();
    }

    public Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken) =>
        IngestUploadedFileAsync(integrationId, filePath, filePath, null, cancellationToken);

    public Task<AttendanceIngestionResult?> IngestUploadedFileAsync(int integrationId, string filePath, string fileName, int? uploadedById, CancellationToken cancellationToken) =>
        IngestFileAsync(integrationId, null, filePath, AttendanceBatchType.ManualUpload, new UploadedFileSource(fileName, uploadedById), cancellationToken);

    public Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, int? connectionId, string filePath, CancellationToken cancellationToken) =>
        IngestFileAsync(integrationId, connectionId, filePath, AttendanceBatchType.Webhook, null, cancellationToken);

    public async Task<PagedResult<UploadedFileItem>> ListUploadedFilesAsync(int? integrationId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var uploads = _db.AttendanceUploadedFiles.AsNoTracking().Where(f => integrationId == null || f.IntegrationId == integrationId);

        // The total is counted with the same filter as the page; a page past the end is answered with the last page.
        var total = await uploads.CountAsync(cancellationToken);
        var current = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        var files = await uploads
            .OrderByDescending(f => f.Id)
            .Skip((current - 1) * size)
            .Take(size)
            .Select(f => new
            {
                f.Id,
                f.IntegrationId,
                IntegrationName = f.Integration!.DisplayName,
                f.FileName,
                f.FileSizeBytes,
                f.Sha256,
                f.UploadedAt,
                UploadedByName = f.UploadedBy == null ? string.Empty : f.UploadedBy.Title,
                f.BatchId,
                Status = f.Batch!.Status,
                BatchType = f.Batch.BatchType,
                f.Batch.RecordsIngested,
                f.Batch.RecordsFailed,
            })
            .ToListAsync(cancellationToken);

        // A file repeats an earlier upload when a lower-numbered row has the same hash.
        var hashes = files.Select(f => f.Sha256).Distinct().ToList();
        var firstIds = (await _db.AttendanceUploadedFiles.AsNoTracking()
                .Where(f => hashes.Contains(f.Sha256))
                .GroupBy(f => f.Sha256)
                .Select(g => new { Sha256 = g.Key, FirstId = g.Min(f => f.Id) })
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.Sha256, g => g.FirstId, StringComparer.Ordinal);

        var processed = await UploadDeletionRules.BatchesWithProcessedPunchesAsync(_db, files.Select(f => f.BatchId).ToList(), cancellationToken);
        var items = files
            .Select(f => new UploadedFileItem
            {
                Id = f.Id,
                IntegrationId = f.IntegrationId,
                IntegrationName = f.IntegrationName,
                FileName = f.FileName,
                FileSizeBytes = f.FileSizeBytes,
                Sha256 = f.Sha256,
                UploadedAt = f.UploadedAt,
                UploadedByName = f.UploadedByName,
                BatchId = f.BatchId,
                Status = f.Status.ToString(),
                RecordsIngested = f.RecordsIngested,
                RecordsFailed = f.RecordsFailed,
                RepeatsAnEarlierUpload = firstIds[f.Sha256] != f.Id,
                CanDelete = UploadDeletionRules.BlockedReason(f.BatchType, f.Status, f.RecordsIngested, f.RecordsFailed, firstIds[f.Sha256] != f.Id, processed.Contains(f.BatchId)) is null,
            })
            .ToList();

        return new PagedResult<UploadedFileItem> { Items = items, Total = total, Page = current, PageSize = size };
    }

    private static AttendanceIngestionResult ToResult(AttendanceProcessingBatch batch, string? sha256 = null, DateTimeOffset? sameFileUploadedAt = null) => new()
    {
        BatchId = batch.Id,
        Status = batch.Status.ToString(),
        RecordsIngested = batch.RecordsIngested,
        RecordsFailed = batch.RecordsFailed,
        ErrorSummary = batch.ErrorSummary,
        Sha256 = sha256,
        SameFileUploadedAt = sameFileUploadedAt,
    };

    private static string FileNameOf(string name)
    {
        // The name as the uploader's computer sent it: drop any folder, whichever separator it used.
        var leaf = name[(name.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        leaf = leaf.Length == 0 ? "unnamed" : leaf;
        return leaf.Length <= 255 ? leaf : leaf[..255];
    }

    private static async Task<string> Sha256OfAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>The layout shown for an integration that has no file format: there is nothing to read its files by yet.</summary>
    private static VendorMappingGuide NoFormatLayout(string vendorKey, string connectorName) => new()
    {
        VendorKey = vendorKey,
        Summary = $"{connectorName} has no default payload format, so its files can't be read yet. A platform administrator must give the connector one before a file is uploaded.",
    };

    /// <summary>Reads an uploaded file by the integration's file format, else its connector's default. An integration with neither has no way to read a file.</summary>
    private async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadUploadAsync(AttendanceIntegration integration, ConnectorInfo connector, AttendancePayloadFormat? format, string filePath, CancellationToken cancellationToken)
    {
        if (format is null)
        {
            throw new InvalidOperationException(PayloadFormatResolution.EffectiveFormatName(AttendanceTransport.FileUpload, connector.DefaultPayloadFormatName) is { } missing
                ? $"File format '{missing}' is not in the catalog, so the files of integration '{integration.DisplayName}' can't be read."
                : $"Connector '{connector.ConnectorKey}' has no default payload format, so the files of integration '{integration.DisplayName}' can't be read. A platform administrator must give the connector one.");
        }

        var reader = _services.GetRequiredService<ICustomFormatFileReader>();
        if (connector.EncryptionKind == PayloadEncryptionKind.None)
        {
            return await reader.ReadAsync(format, filePath, cancellationToken);
        }

        // An encrypted file is decrypted to a temporary file that exists only for the read. The key is resolved for this call, kept in
        // no field and put in no message.
        var decryptor = _services.GetService<IPayloadDecryptorCatalog>()?.Find(connector.EncryptionKind)
            ?? throw new InvalidOperationException($"This server has no way to decrypt a {connector.EncryptionKind} payload.");
        var key = await _services.GetRequiredService<ISecretStore>().GetSecretAsync(integration.PayloadKeyRef, cancellationToken)
            ?? throw new InvalidOperationException($"The decryption key '{integration.PayloadKeyRef}' is not configured, so the file can't be read.");
        var plainPath = Path.Combine(Path.GetTempPath(), $"robeem-{Guid.NewGuid():N}.tmp");
        try
        {
            await decryptor.DecryptAsync(filePath, plainPath, key, cancellationToken);
            return await reader.ReadAsync(format, plainPath, cancellationToken);
        }
        finally
        {
            File.Delete(plainPath);
        }
    }

    private async Task<AttendanceIngestionResult?> IngestFileAsync(int integrationId, int? connectionId, string filePath, AttendanceBatchType batchType, UploadedFileSource? upload, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .Include(i => i.Connections)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return null;
        }

        // What reads the data is the prime tenant's catalog. A file is read by the latest version of its format, and the batch
        // records which version that was, so a punch normalized again later is read the way it was first read.
        var connector = await _catalog.GetConnectorAsync(integration.ConnectorKey, cancellationToken);
        AttendancePayloadFormat? uploadFormat = null;
        if (batchType == AttendanceBatchType.ManualUpload
            && PayloadFormatResolution.EffectiveFormatName(AttendanceTransport.FileUpload, connector?.DefaultPayloadFormatName) is { } formatName)
        {
            uploadFormat = await _catalog.GetLatestFormatAsync(formatName, cancellationToken);
        }

        // The connection the data came through: the one named (a webhook), else the integration's default.
        var connection = connectionId is { } named
            ? integration.Connections.FirstOrDefault(c => c.Id == named)
            : integration.Connections.FirstOrDefault(c => c.IsDefault);

        // The batch is saved first, so a run that dies half-way is still visible. A person's upload
        // is recorded with it, in the same save, whether or not its rows turn out to be readable.
        var batch = new AttendanceProcessingBatch
        {
            IntegrationId = integration.Id,
            ConnectionId = connection?.Id,
            PayloadFormatName = uploadFormat?.Name,
            PayloadFormatVersion = uploadFormat?.Version,
            BatchType = batchType,
        };
        _db.AttendanceProcessingBatches.Add(batch);
        string? sha256 = null;
        DateTimeOffset? sameFileUploadedAt = null;

        // A file that can't be read can't be hashed; the batch still runs and fails on its own, with the reason.
        if (upload is not null && File.Exists(filePath))
        {
            sha256 = await Sha256OfAsync(filePath, cancellationToken);

            // The token's employee id need not name a profile of this tenant; the upload is recorded either way.
            var uploadedById = upload.UploadedById is { } id && await _db.Profiles.AnyAsync(p => p.Id == id, cancellationToken) ? upload.UploadedById : null;
            sameFileUploadedAt = (await _db.AttendanceUploadedFiles.AsNoTracking()
                .Where(f => f.Sha256 == sha256)
                .OrderBy(f => f.Id)
                .Select(f => f.UploadedAt)
                .Take(1)
                .ToListAsync(cancellationToken))
                .Select(at => (DateTimeOffset?)at)
                .FirstOrDefault();
            _db.AttendanceUploadedFiles.Add(new AttendanceUploadedFile
            {
                IntegrationId = integration.Id,
                Batch = batch,
                FileName = FileNameOf(upload.FileName),
                FileSizeBytes = new FileInfo(filePath).Length,
                Sha256 = sha256,
                UploadedAt = DateTimeOffset.UtcNow,
                UploadedById = uploadedById,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // The vendor's key is what the raw rows record as their source; the connector's adapter key picks the code that reads them.
        try
        {
            var info = connector ?? throw new InvalidOperationException($"Connector '{integration.ConnectorKey}' is not in the catalog, so the integration can't be read.");
            var vendorKey = info.VendorKey;
            IReadOnlyList<ExternalAttendanceTransaction> transactions;
            if (batchType == AttendanceBatchType.ManualUpload)
            {
                transactions = await ReadUploadAsync(integration, info, uploadFormat, filePath, cancellationToken);
            }
            else
            {
                // Adapters are keyed services, one per AttendanceConnectorDefinition.AdapterKey (CLAUDE.md §1.9).
                var adapterKey = info.AdapterKey;
                var adapter = (adapterKey is { Length: > 0 } ? _services.GetKeyedService<IAttendanceAdapter>(adapterKey) : null)
                    ?? throw new InvalidOperationException($"No adapter is registered for connector '{info.ConnectorKey}' (adapter key '{adapterKey}').");
                var config = new AttendanceIntegrationConfig(integration.Id, vendorKey, connection?.SecretRef ?? string.Empty)
                {
                    ConnectionId = connection?.Id,
                    Settings = ConnectionSettingsValidator.Parse(connection?.Settings),
                    UploadFilePath = filePath,
                };
                transactions = (await adapter.RetrieveAttendanceTransactionsAsync(config, null, cancellationToken)).Transactions;
            }

            await _writer.AddTransactionsAsync(integration, batch, vendorKey, transactions, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResult(batch, sha256, sameFileUploadedAt);
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

            return ToResult(failed, sha256, sameFileUploadedAt);
        }
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

    /// <summary>Who sent an uploaded file, and under what name.</summary>
    private sealed record UploadedFileSource(string FileName, int? UploadedById);
}
