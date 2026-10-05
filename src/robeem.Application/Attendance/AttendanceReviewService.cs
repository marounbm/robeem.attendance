using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceReviewService : IAttendanceReviewService
{
    private const int ReasonMaxLength = 1000;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceReviewService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
        _clock = clock;
    }

    public async Task<ReviewResult> DecideAsync(int recordId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken)
    {
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

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (!approved && trimmed is null)
        {
            return ReviewResult.Invalid(nameof(DecideReviewRequest.Reason), "A reason is required to reject a record.");
        }

        if (trimmed is { Length: > ReasonMaxLength })
        {
            return ReviewResult.Invalid(nameof(DecideReviewRequest.Reason), $"The reason can be at most {ReasonMaxLength} characters.");
        }

        var employee = await _db.Profiles.AsNoTracking().SingleAsync(p => p.Id == record.EmployeeId, cancellationToken);
        if (!await _db.Profiles.AnyAsync(p => p.Id == decidedByEmployeeId, cancellationToken))
        {
            return ReviewResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (decidedByEmployeeId == record.EmployeeId)
        {
            return ReviewResult.Invalid(CorrectionErrorKeys.DecidedBy, "A record can't be reviewed by its own employee.");
        }

        // A manager reviews their own direct reports; HR reviews anyone.
        if (!asHr && employee.ManagerId != decidedByEmployeeId)
        {
            return ReviewResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager, or HR, can review this record.");
        }

        // A shift with an exception that is not closed is never approved (CLAUDE.md §1.5): the exception comes first.
        var exceptionOpen = await _db.AttendanceExceptions.AnyAsync(
            e => e.EmployeeId == record.EmployeeId
                && e.AttendanceDate == record.AttendanceDate
                && e.TimeScheduleShiftId == record.TimeScheduleShiftId
                && e.Status != AttendanceExceptionStatus.Closed,
            cancellationToken);
        if (exceptionOpen)
        {
            return ReviewResult.Invalid(nameof(recordId), "An exception of this shift is still open. Settle it first.");
        }

        var role = asHr ? AttendanceReviewerRole.HR : AttendanceReviewerRole.Manager;
        var outcome = approved ? AttendanceReviewOutcome.Approved : AttendanceReviewOutcome.Rejected;

        // The decision, the status, the audit entry and the day's summary are one unit.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var decision = new AttendanceReviewDecision
        {
            AttendanceRecordId = record.Id,
            EmployeeId = record.EmployeeId,
            AttendanceDate = record.AttendanceDate,
            Outcome = outcome,
            DecidedById = decidedByEmployeeId,
            DecidedByRole = role,
            DecidedAt = _clock.GetLocalNow().DateTime,
            Reason = trimmed,
            OvertimeMinutes = record.OvertimeMinutes,
        };
        _db.AttendanceReviewDecisions.Add(decision);
        record.Status = approved ? AttendanceRecordStatus.Approved : AttendanceRecordStatus.Rejected;
        _audit.Record(
            AttendanceAuditActions.ReviewDecided,
            nameof(AttendanceRecord),
            record.Id,
            $"{outcome} by {role}; overtime {record.OvertimeMinutes} min.{(trimmed is null ? string.Empty : " Reason: " + trimmed)}",
            $"employee:{decidedByEmployeeId}");
        await _db.SaveChangesAsync(cancellationToken);

        await _engine.ProcessEmployeeDayAsync(record.EmployeeId, record.AttendanceDate, cancellationToken);

        // If the day changed since the reviewer looked (a late punch), the recalculation wrote a new
        // version and this decision would cover a record nobody reads. Keep nothing; ask again.
        if (!await _db.AttendanceRecords.AsNoTracking().AnyAsync(r => r.Id == recordId && r.IsCurrent, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return ReviewResult.Invalid(nameof(recordId), "The day changed while it was being reviewed. Review it again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return ReviewResult.Success(decision, record.Status);
    }

    public async Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(int approverEmployeeId, bool isHr, CancellationToken cancellationToken)
    {
        var records = _db.AttendanceRecords
            .AsNoTracking()
            .Where(r => r.IsCurrent
                && r.Status == AttendanceRecordStatus.PendingReview
                && r.EmployeeId != approverEmployeeId
                && !_db.AttendanceExceptions.Any(e => e.EmployeeId == r.EmployeeId
                    && e.AttendanceDate == r.AttendanceDate
                    && e.TimeScheduleShiftId == r.TimeScheduleShiftId
                    && e.Status != AttendanceExceptionStatus.Closed));

        if (!isHr)
        {
            records = records.Where(r => r.Employee!.ManagerId == approverEmployeeId);
        }

        var rows = await records
            .OrderBy(r => r.AttendanceDate)
            .ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.EmployeeId,
                r.Employee!.FirstName,
                r.Employee.LastName,
                r.AttendanceDate,
                r.TimeScheduleShiftId,
                r.ScheduledStart,
                r.ScheduledEnd,
                r.ActualCheckIn,
                r.ActualCheckOut,
                r.WorkingHours,
                r.OvertimeMinutes,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ReviewListItem
            {
                RecordId = r.Id,
                EmployeeId = r.EmployeeId,
                EmployeeName = $"{r.FirstName} {r.LastName}",
                AttendanceDate = r.AttendanceDate,
                ScheduledStart = r.ScheduledStart,
                ScheduledEnd = r.ScheduledEnd,
                ActualCheckIn = r.ActualCheckIn,
                ActualCheckOut = r.ActualCheckOut,
                WorkingHours = r.WorkingHours,
                OvertimeMinutes = r.OvertimeMinutes,
                Reason = r.OvertimeMinutes > 0 ? "Overtime"
                    : r.TimeScheduleShiftId is null ? "UnscheduledWork"
                    : r.ActualCheckIn is null || r.ActualCheckOut is null ? "IncompleteDay"
                    : "Review",
            })
            .ToList();
    }
}
