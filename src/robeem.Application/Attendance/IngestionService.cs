using System.Security.Cryptography;
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

    public async Task<IReadOnlyList<UploadTarget>> ListUploadTargetsAsync(CancellationToken cancellationToken)
    {
        // Any provider may take manual uploads: the integration's own transport decides.
        var integrations = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Status == AttendanceIntegrationStatus.Active && i.Transport == AttendanceTransport.FileUpload && i.Provider!.IsActive)
            .OrderBy(i => i.DisplayName)
            .Select(i => new { i.Id, i.DisplayName, i.Transport, i.FileFormatId, Provider = i.Provider! })
            .ToListAsync(cancellationToken);

        var effectiveFormatIds = integrations
            .Select(i => FileFormatResolution.EffectiveFormatId(i.Transport, i.FileFormatId, i.Provider.DefaultFileFormatId))
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var formats = effectiveFormatIds.Count == 0
            ? []
            : await _db.AttendanceFileFormats.AsNoTracking().Include(f => f.EventCodes).Where(f => effectiveFormatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        return integrations
            .Select(i =>
            {
                var layout = FileFormatResolution.EffectiveFormatId(i.Transport, i.FileFormatId, i.Provider.DefaultFileFormatId) is { } formatId && formats.TryGetValue(formatId, out var format)
                    ? FileFormatLayout.Describe(format, i.Provider.ProviderKey)
                    : NoFormatLayout(i.Provider);
                return new UploadTarget
                {
                    IntegrationId = i.Id,
                    DisplayName = i.DisplayName,
                    ProviderKey = i.Provider.ProviderKey,
                    ProviderName = i.Provider.DisplayName,
                    FileFormats = layout.FileFormats,
                    Layout = layout,
                };
            })
            .ToList();
    }

    public Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken) =>
        IngestUploadedFileAsync(integrationId, filePath, filePath, null, cancellationToken);

    public Task<AttendanceIngestionResult?> IngestUploadedFileAsync(int integrationId, string filePath, string fileName, int? uploadedById, CancellationToken cancellationToken) =>
        IngestFileAsync(integrationId, filePath, AttendanceBatchType.ManualUpload, new UploadedFileSource(fileName, uploadedById), cancellationToken);

    public Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, string filePath, CancellationToken cancellationToken) =>
        IngestFileAsync(integrationId, filePath, AttendanceBatchType.Webhook, null, cancellationToken);

    public async Task<IReadOnlyList<UploadedFileItem>> ListUploadedFilesAsync(int? integrationId, int take, CancellationToken cancellationToken)
    {
        var files = await _db.AttendanceUploadedFiles.AsNoTracking()
            .Where(f => integrationId == null || f.IntegrationId == integrationId)
            .OrderByDescending(f => f.Id)
            .Take(Math.Clamp(take, 1, 500))
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

        return files
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
            })
            .ToList();
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
    private static ProviderMappingGuide NoFormatLayout(AttendanceProvider provider) => new()
    {
        ProviderKey = provider.ProviderKey,
        Summary = $"{provider.DisplayName} has no file format of its own, and this integration has none chosen. Edit the integration and choose a file format (or give the provider a default one) before uploading a file.",
    };

    /// <summary>Reads an uploaded file by the integration's file format, else its provider's default. An integration with neither has no way to read a file.</summary>
    private async Task<IReadOnlyList<ExternalAttendanceTransaction>> ReadUploadAsync(AttendanceIntegration integration, string providerKey, string filePath, CancellationToken cancellationToken)
    {
        var formatId = FileFormatResolution.EffectiveFormatId(integration.Transport, integration.FileFormatId, integration.Provider!.DefaultFileFormatId)
            ?? throw new InvalidOperationException($"Integration '{integration.DisplayName}' has no file format, and provider '{providerKey}' has no default one, so its files can't be read. Choose a file format for the integration.");
        var format = await _db.AttendanceFileFormats.AsNoTracking().Include(f => f.EventCodes).SingleAsync(f => f.Id == formatId, cancellationToken);
        return await _services.GetRequiredService<ICustomFormatFileReader>().ReadAsync(format, filePath, cancellationToken);
    }

    private async Task<AttendanceIngestionResult?> IngestFileAsync(int integrationId, string filePath, AttendanceBatchType batchType, UploadedFileSource? upload, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .Include(i => i.Provider)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return null;
        }

        // The batch is saved first, so a run that dies half-way is still visible. A person's upload
        // is recorded with it, in the same save, whether or not its rows turn out to be readable.
        var batch = new AttendanceProcessingBatch { IntegrationId = integration.Id, BatchType = batchType };
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

        var providerKey = integration.Provider!.ProviderKey;
        try
        {
            IReadOnlyList<ExternalAttendanceTransaction> transactions;
            if (batchType == AttendanceBatchType.ManualUpload)
            {
                transactions = await ReadUploadAsync(integration, providerKey, filePath, cancellationToken);
            }
            else
            {
                // Adapters are keyed services, one per AttendanceProvider.ProviderKey (CLAUDE.md §1.9).
                var provider = _services.GetKeyedService<IAttendanceProvider>(providerKey)
                    ?? throw new InvalidOperationException($"No adapter is registered for provider '{providerKey}'.");
                var config = new AttendanceIntegrationConfig(integration.Id, providerKey, integration.ConnectionConfigRef) { UploadFilePath = filePath };
                transactions = (await provider.RetrieveAttendanceTransactionsAsync(config, null, cancellationToken)).Transactions;
            }

            await _writer.AddTransactionsAsync(integration, batch, providerKey, transactions, cancellationToken);
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
