using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceApprovalReconciler : IAttendanceApprovalReconciler
{
    private readonly IRobeemDbContext _db;
    private readonly TimeProvider _clock;

    public AttendanceApprovalReconciler(IRobeemDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ApprovalReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        // A pending approval of a record that is retired, or no longer waiting for review, has nothing left to decide.
        var stale = await _db.AttendanceApprovals
            .Where(a => a.Type == AttendanceApprovalType.Review
                && a.Status == AttendanceApprovalStatus.Pending
                && (!a.AttendanceRecord!.IsCurrent || a.AttendanceRecord.Status != AttendanceRecordStatus.PendingReview))
            .ToListAsync(cancellationToken);
        stale.ForEach(a => a.Status = AttendanceApprovalStatus.Superseded);

        // A current record waiting for review with no pending approval cannot be found by an approver.
        var unqueued = await _db.AttendanceRecords
            .Where(r => r.IsCurrent
                && r.Status == AttendanceRecordStatus.PendingReview
                && !_db.AttendanceApprovals.Any(a => a.AttendanceRecordId == r.Id && a.Status == AttendanceApprovalStatus.Pending))
            .ToListAsync(cancellationToken);
        var now = _clock.GetLocalNow().DateTime;
        _db.AttendanceApprovals.AddRange(unqueued.Select(r => ApprovalRules.NewReviewApproval(r, now)));

        if (stale.Count > 0 || unqueued.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new ApprovalReconciliation(unqueued.Count, stale.Count);
    }
}
