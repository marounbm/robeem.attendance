using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class NormalizedTransactionRecorder : INormalizedTransactionRecorder
{
    private readonly IRobeemDbContext _db;

    public NormalizedTransactionRecorder(IRobeemDbContext db)
    {
        _db = db;
    }

    public async Task<NormalizedAttendanceTransaction> RecordAsync(NormalizedAttendanceTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var original = transaction.EmployeeId is null ? null : await FindOriginalIdAsync(transaction, cancellationToken);
        MarkDuplicate(transaction, original);

        _db.NormalizedAttendanceTransactions.Add(transaction);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (transaction.DuplicateStatus == DuplicateStatus.Unique && transaction.EmployeeId is not null)
        {
            // Lost a race with a concurrent save of the same key (unique index)? Then this row is the
            // duplicate. The entry is still Added, so saving again retries with the new status.
            original = await FindOriginalIdAsync(transaction, cancellationToken);
            if (original is null)
            {
                throw;
            }

            MarkDuplicate(transaction, original);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return transaction;
    }

    private static void MarkDuplicate(NormalizedAttendanceTransaction transaction, int? originalId)
    {
        transaction.DuplicateStatus = originalId is null ? DuplicateStatus.Unique : DuplicateStatus.DuplicateConfirmed;
        transaction.DuplicateOfTransactionId = originalId;
    }

    private async Task<int?> FindOriginalIdAsync(NormalizedAttendanceTransaction transaction, CancellationToken cancellationToken)
    {
        // TenantId is part of the key through the query filter. DeviceId may be null (e.g. web
        // portal punches); EF compares nulls as equal here.
        return await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.DuplicateStatus == DuplicateStatus.Unique
                && n.EmployeeId == transaction.EmployeeId
                && n.DeviceId == transaction.DeviceId
                && n.NormalizedEventType == transaction.NormalizedEventType
                && n.DuplicateBucketTimestampUtc == transaction.DuplicateBucketTimestampUtc)
            .OrderBy(n => n.Id)
            .Select(n => (int?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
