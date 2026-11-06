using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceReviewService : IAttendanceReviewService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceApprovalService _approvals;
    private readonly TimeProvider _clock;

    public AttendanceReviewService(IRobeemDbContext db, IAttendanceApprovalService approvals, TimeProvider clock)
    {
        _db = db;
        _approvals = approvals;
        _clock = clock;
    }

    public async Task<ReviewResult> DecideAsync(int recordId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken)
    {
        // The decision is the record's review approval's; the record is how a caller names it.
        var record = await _db.AttendanceRecords.SingleOrDefaultAsync(r => r.Id == recordId, cancellationToken);
        if (record is null)
        {
            return ReviewResult.Missing();
        }

        if (!record.IsCurrent)
        {
            return ReviewResult.Invalid(nameof(recordId), "This version of the record was replaced by a recalculation. Review the current one.");
        }

        if (record.Status != AttendanceRecordStatus.PendingReview)
        {
            return ReviewResult.Invalid(nameof(recordId), $"The record is {record.Status}, not waiting for review.");
        }

        var approvalId = await _db.AttendanceApprovals
            .Where(a => a.AttendanceRecordId == recordId && a.Status == AttendanceApprovalStatus.Pending)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (approvalId is null)
        {
            // The engine queues one whenever it leaves a record for review; a record without one is made good here, so it can be decided.
            var created = ApprovalRules.NewReviewApproval(record, _clock.GetLocalNow().DateTime);
            _db.AttendanceApprovals.Add(created);
            await _db.SaveChangesAsync(cancellationToken);
            approvalId = created.Id;
        }

        var result = await _approvals.DecideAsync(approvalId.Value, decidedByEmployeeId, asHr, approved, reason, cancellationToken);
        if (result.NotFound)
        {
            return ReviewResult.Missing();
        }

        if (!result.Succeeded)
        {
            var (field, message) = result.Errors.First();
            return ReviewResult.Invalid(field == ApprovalErrorKeys.Approval ? nameof(recordId) : field, message);
        }

        var outcome = result.Status == AttendanceApprovalStatus.Approved ? AttendanceReviewOutcome.Approved : AttendanceReviewOutcome.Rejected;
        return ReviewResult.Success(result.ApprovalId!.Value, outcome, result.RecordStatus!.Value);
    }

    public async Task<PagedResult<ReviewListItem>> ListPendingAsync(int approverEmployeeId, bool isHr, ReviewFilter filter, CancellationToken cancellationToken)
    {
        // A manager gets their direct reports'; HR gets every employee's.
        var approvalFilter = new ApprovalFilter(filter.EmployeeId, filter.From, filter.To, AttendanceApprovalType.Review, filter.Reason, filter.Page, filter.PageSize);
        var result = await _approvals.ListPendingAsync(approverEmployeeId, !isHr, isHr, approvalFilter, cancellationToken);
        return new PagedResult<ReviewListItem>
        {
            Items = result.Items.Select(i => i.Review!).ToList(),
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize,
        };
    }
}
