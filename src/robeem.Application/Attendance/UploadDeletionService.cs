using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class UploadDeletionService : IUploadDeletionService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;

    public UploadDeletionService(IRobeemDbContext db, IAttendanceAuditTrail audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<UploadDeletionResult> DeleteAsync(int uploadId, CancellationToken cancellationToken)
    {
        var upload = await _db.AttendanceUploadedFiles.Include(f => f.Batch).SingleOrDefaultAsync(f => f.Id == uploadId, cancellationToken);
        if (upload?.Batch is not { } batch)
        {
            return new UploadDeletionResult(UploadDeletionOutcome.NotFound);
        }

        var repeats = await _db.AttendanceUploadedFiles.AnyAsync(f => f.Sha256 == upload.Sha256 && f.Id < upload.Id, cancellationToken);
        var processed = await UploadDeletionRules.BatchesWithProcessedPunchesAsync(_db, [batch.Id], cancellationToken);
        var reason = UploadDeletionRules.BlockedReason(batch.BatchType, batch.Status, batch.RecordsIngested, batch.RecordsFailed, repeats, processed.Contains(batch.Id));
        if (reason is not null)
        {
            return new UploadDeletionResult(UploadDeletionOutcome.NotAllowed, reason);
        }

        // Nothing else may hold the batch: its only owner is this upload, and its only rows are the punches below.
        var punches = await _db.RawAttendanceTransactions.Where(r => r.ImportBatchId == batch.Id).ToListAsync(cancellationToken);
        var failed = UploadDeletionRules.Failed(batch.Status, batch.RecordsIngested, batch.RecordsFailed);

        // The deletion, its permission and its audit entry are one save: all happen or none does.
        _db.AllowUnprocessedUploadDeletion(batch.Id);
        _db.RawAttendanceTransactions.RemoveRange(punches);
        _db.AttendanceUploadedFiles.Remove(upload);
        _db.AttendanceProcessingBatches.Remove(batch);
        _audit.Record(
            AttendanceAuditActions.UploadDeleted,
            nameof(AttendanceUploadedFile),
            upload.Id,
            $"Deleted '{upload.FileName}' (SHA-256 {upload.Sha256}, integration {upload.IntegrationId}, batch {batch.Id}) as {(failed ? "a failed upload" : "a repeat of an earlier upload")} with its {punches.Count} unprocessed punch(es).");
        await _db.SaveChangesAsync(cancellationToken);
        return new UploadDeletionResult(UploadDeletionOutcome.Deleted, PunchesDeleted: punches.Count);
    }
}
