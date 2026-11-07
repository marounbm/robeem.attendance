using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// When an uploaded file may be deleted with its punches (the one exception to the immutability of raw attendance, CLAUDE.md §1.2): the
/// file failed or repeats an earlier one, and not one of its punches has been processed. Shared by the upload list, which offers the
/// button, and by the deletion itself, which decides.
/// </summary>
internal static class UploadDeletionRules
{
    /// <summary>
    /// The batches, among <paramref name="batchIds"/>, with a punch that was processed: one that is no longer Pending or Failed, has a normalized
    /// punch made from it (also a replaced one), or is no longer active in the retention sense. Such a punch is a record of what the clock sent and is kept.
    /// </summary>
    public static async Task<HashSet<int>> BatchesWithProcessedPunchesAsync(IRobeemDbContext db, IReadOnlyCollection<int> batchIds, CancellationToken cancellationToken)
    {
        var batches = await db.RawAttendanceTransactions.AsNoTracking()
            .Where(r => batchIds.Contains(r.ImportBatchId)
                && ((r.ProcessingStatus != RawTransactionProcessingStatus.Pending && r.ProcessingStatus != RawTransactionProcessingStatus.Failed)
                    || r.RetentionStatus != RawTransactionRetentionStatus.Active
                    || db.NormalizedAttendanceTransactions.Any(n => n.RawTransactionId == r.Id)))
            .Select(r => r.ImportBatchId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. batches];
    }

    /// <summary>Whether the upload counts as failed: the whole file could not be read, or every punch in it was unusable.</summary>
    public static bool Failed(AttendanceBatchStatus status, int recordsIngested, int recordsFailed) =>
        status == AttendanceBatchStatus.Failed || (recordsIngested > 0 && recordsFailed >= recordsIngested);

    /// <summary>Why the upload may not be deleted, or null when it may.</summary>
    public static string? BlockedReason(AttendanceBatchType type, AttendanceBatchStatus status, int recordsIngested, int recordsFailed, bool repeatsAnEarlierUpload, bool hasProcessedPunches)
    {
        if (type != AttendanceBatchType.ManualUpload)
        {
            return "Only an uploaded file can be deleted.";
        }

        if (status == AttendanceBatchStatus.Running)
        {
            return "The file is still being read.";
        }

        if (!Failed(status, recordsIngested, recordsFailed) && !repeatsAnEarlierUpload)
        {
            return "The file was read and is not a repeat of an earlier one, so its punches are kept.";
        }

        return hasProcessedPunches
            ? "Some of the file's punches were already processed, so they are kept as the record of what the clock sent."
            : null;
    }
}
