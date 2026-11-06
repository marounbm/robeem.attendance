using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceApprovalService : IAttendanceApprovalService
{
    private const int ReasonMaxLength = 1000;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceApprovalService(IRobeemDbContext db, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _engine = engine;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PagedResult<ApprovalListItem>> ListPendingAsync(int approverEmployeeId, bool isManager, bool isHr, ApprovalFilter filter, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
        var all = _db.AttendanceApprovals.AsNoTracking();

        // What this approver may decide, by kind. A review reason names a review, so it leaves the corrections out.
        var branches = new List<IQueryable<AttendanceApproval>>();
        if (filter.Type != AttendanceApprovalType.Review && filter.Reason is null)
        {
            if (isManager)
            {
                branches.Add(Narrow(ApprovalRules.CorrectionsAwaitingManager(all, approverEmployeeId), filter));
            }

            if (isHr)
            {
                branches.Add(Narrow(ApprovalRules.CorrectionsAwaitingHr(all), filter));
            }
        }

        if (filter.Type != AttendanceApprovalType.Correction && (isManager || isHr))
        {
            branches.Add(NarrowReviews(Narrow(ApprovalRules.ReviewsFor(_db, all, approverEmployeeId, isHr), filter), filter.Reason));
        }

        if (branches.Count == 0)
        {
            return new PagedResult<ApprovalListItem> { Items = [], Total = 0, Page = 1, PageSize = pageSize };
        }

        var visible = branches.Aggregate((left, right) => left.Union(right));

        // The total is counted with the same filters as the page; a page past the end is answered with the last page.
        var total = await visible.CountAsync(cancellationToken);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var pageIds = await visible
            .OrderBy(a => a.AttendanceDate)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var items = await LoadItemsAsync(pageIds, approverEmployeeId, isManager, isHr, cancellationToken);
        return new PagedResult<ApprovalListItem> { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<ApprovalResult> DecideAsync(int approvalId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken)
    {
        var approval = await _db.AttendanceApprovals.SingleOrDefaultAsync(a => a.Id == approvalId, cancellationToken);
        if (approval is null)
        {
            return ApprovalResult.Missing();
        }

        return approval.Type == AttendanceApprovalType.Correction
            ? await DecideCorrectionAsync(approval, decidedByEmployeeId, asHr, approved, cancellationToken)
            : await DecideReviewAsync(approval, decidedByEmployeeId, asHr, approved, reason, cancellationToken);
    }

    private static IQueryable<AttendanceApproval> Narrow(IQueryable<AttendanceApproval> approvals, ApprovalFilter filter)
    {
        if (filter.EmployeeId is { } employeeId)
        {
            approvals = approvals.Where(a => a.EmployeeId == employeeId);
        }

        if (filter.From is { } from)
        {
            var first = from.Date;
            approvals = approvals.Where(a => a.AttendanceDate >= first);
        }

        if (filter.To is { } to)
        {
            var last = to.Date;
            approvals = approvals.Where(a => a.AttendanceDate <= last);
        }

        return approvals;
    }

    // The reason is worked out, not stored (see ApprovalRules.ReasonOf): overtime first, then no shift, then a missing punch.
    private static IQueryable<AttendanceApproval> NarrowReviews(IQueryable<AttendanceApproval> reviews, ReviewReason? reason) => reason switch
    {
        null => reviews,
        ReviewReason.Overtime => reviews.Where(a => a.AttendanceRecord!.OvertimeMinutes > 0),
        ReviewReason.UnscheduledWork => reviews.Where(a => a.AttendanceRecord!.OvertimeMinutes <= 0 && a.AttendanceRecord.TimeScheduleShiftId == null),
        ReviewReason.IncompleteDay => reviews.Where(a => a.AttendanceRecord!.OvertimeMinutes <= 0
            && a.AttendanceRecord.TimeScheduleShiftId != null
            && (a.AttendanceRecord.ActualCheckIn == null || a.AttendanceRecord.ActualCheckOut == null)),
        _ => reviews.Where(a => a.AttendanceRecord!.OvertimeMinutes <= 0
            && a.AttendanceRecord.TimeScheduleShiftId != null
            && a.AttendanceRecord.ActualCheckIn != null
            && a.AttendanceRecord.ActualCheckOut != null),
    };

    private static string ActorOf(int employeeId) => $"employee:{employeeId}";

    private async Task<List<ApprovalListItem>> LoadItemsAsync(List<int> pageIds, int approverEmployeeId, bool isManager, bool isHr, CancellationToken cancellationToken)
    {
        if (pageIds.Count == 0)
        {
            return [];
        }

        var rows = await _db.AttendanceApprovals
            .AsNoTracking()
            .Where(a => pageIds.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Type,
                a.EmployeeId,
                a.Employee!.FirstName,
                a.Employee.LastName,
                ManagerId = a.Employee.ManagerId,
                a.AttendanceDate,
                a.RequestedAt,
                a.Workflow,
                a.ManagerDecision,
                a.OvertimeMinutes,
                RecordId = a.AttendanceRecordId,
                ShiftId = a.AttendanceRecord!.TimeScheduleShiftId,
                ScheduledStart = a.AttendanceRecord.ScheduledStart,
                ScheduledEnd = a.AttendanceRecord.ScheduledEnd,
                ActualCheckIn = a.AttendanceRecord.ActualCheckIn,
                ActualCheckOut = a.AttendanceRecord.ActualCheckOut,
                WorkingHours = (TimeSpan?)a.AttendanceRecord.WorkingHours,
            })
            .ToListAsync(cancellationToken);

        var correctionIds = rows.Where(r => r.Type == AttendanceApprovalType.Correction).Select(r => r.Id).ToList();
        var edits = correctionIds.Count == 0
            ? []
            : await _db.AttendanceCorrections
                .AsNoTracking()
                .Where(c => correctionIds.Contains(c.AttendanceApprovalId))
                .OrderBy(c => c.Id)
                .Select(c => new
                {
                    c.Id,
                    c.AttendanceApprovalId,
                    c.AttendanceExceptionId,
                    c.Kind,
                    c.OriginalEventType,
                    c.CorrectedEventType,
                    c.ProposedValue,
                    c.Justification,
                })
                .ToListAsync(cancellationToken);
        var editsOf = edits.ToLookup(e => e.AttendanceApprovalId);

        var byId = rows.ToDictionary(r => r.Id);
        var items = new List<ApprovalListItem>();
        foreach (var id in pageIds)
        {
            var r = byId[id];
            var name = $"{r.FirstName} {r.LastName}";
            if (r.Type == AttendanceApprovalType.Review)
            {
                items.Add(new ApprovalListItem
                {
                    Id = r.Id,
                    Type = nameof(AttendanceApprovalType.Review),
                    EmployeeId = r.EmployeeId,
                    EmployeeName = name,
                    AttendanceDate = r.AttendanceDate,
                    RequestedAt = r.RequestedAt,
                    AwaitingStep = isHr ? "HR" : "Manager",
                    Review = new ReviewListItem
                    {
                        RecordId = r.RecordId ?? 0,
                        EmployeeId = r.EmployeeId,
                        EmployeeName = name,
                        AttendanceDate = r.AttendanceDate,
                        ScheduledStart = r.ScheduledStart,
                        ScheduledEnd = r.ScheduledEnd,
                        ActualCheckIn = r.ActualCheckIn,
                        ActualCheckOut = r.ActualCheckOut,
                        WorkingHours = r.WorkingHours ?? TimeSpan.Zero,
                        OvertimeMinutes = r.OvertimeMinutes,
                        Reason = ApprovalRules.ReasonOf(r.OvertimeMinutes, r.ShiftId, r.ActualCheckIn, r.ActualCheckOut).ToString(),
                    },
                });
                continue;
            }

            // One item per request: its edits are decided together, by the decision on the approval.
            var requestEdits = editsOf[r.Id].ToList();
            var first = requestEdits[0];
            var managerStep = isManager
                && r.ManagerDecision is null
                && r.Workflow is ApprovalWorkflow.Manager or ApprovalWorkflow.Both
                && r.ManagerId == approverEmployeeId;
            items.Add(new ApprovalListItem
            {
                Id = r.Id,
                Type = nameof(AttendanceApprovalType.Correction),
                EmployeeId = r.EmployeeId,
                EmployeeName = name,
                AttendanceDate = r.AttendanceDate,
                RequestedAt = r.RequestedAt,
                AwaitingStep = managerStep ? "Manager" : "HR",
                Correction = new CorrectionListItem
                {
                    Id = first.Id,
                    ExceptionId = first.AttendanceExceptionId,
                    EmployeeId = r.EmployeeId,
                    EmployeeName = name,
                    AttendanceDate = r.AttendanceDate,
                    Kind = first.Kind.ToString(),
                    OriginalEventType = first.OriginalEventType?.ToString(),
                    CorrectedEventType = first.CorrectedEventType.ToString(),
                    ProposedValue = first.ProposedValue,
                    Edits = requestEdits.Select(e => new CorrectionEditItem
                    {
                        Kind = e.Kind.ToString(),
                        OriginalEventType = e.OriginalEventType?.ToString(),
                        CorrectedEventType = e.CorrectedEventType.ToString(),
                        At = e.ProposedValue,
                    }).ToList(),
                    Justification = first.Justification,
                    RequestedAt = r.RequestedAt,
                    ApprovalWorkflow = r.Workflow.ToString(),
                    ManagerDecision = r.ManagerDecision?.ToString(),
                },
            });
        }

        return items;
    }

    private async Task<ApprovalResult> DecideCorrectionAsync(AttendanceApproval approval, int decidedByUserId, bool isHrDecision, bool approved, CancellationToken cancellationToken)
    {
        // A request is decided as a whole: every edit of it is approved, applied or rejected together.
        var group = await _db.AttendanceCorrections.Where(c => c.AttendanceApprovalId == approval.Id).OrderBy(c => c.Id).ToListAsync(cancellationToken);

        // The exceptions waiting for it: those it names and, for a correction of a whole day, the day's that wait for a correction.
        var linked = group.Where(c => c.AttendanceExceptionId != null).Select(c => c.AttendanceExceptionId!.Value).ToList();
        var wholeDay = group.Any(c => c.AttendanceExceptionId == null);
        var exceptions = await _db.AttendanceExceptions
            .Where(e => linked.Contains(e.Id)
                || (wholeDay && e.EmployeeId == approval.EmployeeId && e.AttendanceDate == approval.AttendanceDate && e.Status == AttendanceExceptionStatus.PendingCorrection))
            .ToListAsync(cancellationToken);

        if (approval.Status != AttendanceApprovalStatus.Pending)
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.Approval, $"The correction is {approval.Status}, not waiting for a decision.");
        }

        // The decider is the authenticated user, and the endpoint has checked that they hold the
        // role they decide in; here only the correction's own rules remain.
        if (!await _db.Profiles.AnyAsync(p => p.Id == decidedByUserId, cancellationToken))
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (decidedByUserId == approval.RequestedByEmployeeId)
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "A correction can't be decided by the person who proposed it.");
        }

        // Who may take the manager's step: the employee's own manager. HR takes it only when the
        // employee has no manager, or an inactive one, so that no correction can be left undecidable.
        var employee = await _db.Profiles.AsNoTracking().Include(p => p.Manager).SingleAsync(p => p.Id == approval.EmployeeId, cancellationToken);
        var awaitingManagerStep = approval.Workflow != ApprovalWorkflow.HR && approval.ManagerDecision is null;
        var standsIn = isHrDecision && awaitingManagerStep && ApprovalRules.IsUnmanaged(employee);
        var decidesAsHr = isHrDecision && !standsIn;

        if (ApprovalRules.RoleProblem(approval, decidesAsHr) is { } problem)
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.IsHrDecision, problem);
        }

        if (!decidesAsHr && !standsIn && employee.ManagerId != decidedByUserId)
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager can decide this correction.");
        }

        var now = _clock.GetLocalNow().DateTime;
        var decision = approved ? AttendanceCorrectionDecision.Approved : AttendanceCorrectionDecision.Rejected;
        var actor = ActorOf(decidedByUserId);
        var role = decidesAsHr ? "HR" : standsIn ? "HR, in place of the manager" : "Manager";
        if (decidesAsHr)
        {
            (approval.HrDecision, approval.HrDecidedById, approval.HrDecidedAt) = (decision, decidedByUserId, now);
        }
        else
        {
            (approval.ManagerDecision, approval.ManagerDecidedById, approval.ManagerDecidedAt) = (decision, decidedByUserId, now);
        }

        if (!approved)
        {
            // Back to Open, so a new correction can be proposed (study §11.1).
            approval.Status = AttendanceApprovalStatus.Rejected;
            group.ForEach(row => row.IsPending = false);
            exceptions.ForEach(e => e.Status = AttendanceExceptionStatus.Open);
            _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceApproval), approval.Id, $"{decision} by {role}; the correction is {approval.Status}.", actor);
            exceptions.ForEach(e => _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), e.Id, "PendingCorrection -> Open: the correction was rejected.", actor));
            await _db.SaveChangesAsync(cancellationToken);
            return ApprovalResult.Correction(approval, exceptions.FirstOrDefault()?.Status ?? AttendanceExceptionStatus.Open);
        }

        if (!ApprovalRules.IsComplete(approval, decidesAsHr))
        {
            _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceApproval), approval.Id, $"{decision} by {role}; the correction is still {approval.Status}.", actor);
            await _db.SaveChangesAsync(cancellationToken);
            return ApprovalResult.Correction(approval, exceptions.FirstOrDefault()?.Status ?? AttendanceExceptionStatus.PendingCorrection);
        }

        // Approve, apply, close the exceptions and recalculate the day as one unit: a day recalculated
        // with the exceptions still open would never come out Approved, and a failure must not leave
        // the correction applied to a day that was not recalculated. Whatever the edits did not settle
        // is found again by the recalculation and opens as a new exception.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        approval.Status = AttendanceApprovalStatus.Applied;
        approval.AppliedAt = now;
        group.ForEach(row => row.IsPending = false);

        foreach (var exception in exceptions)
        {
            exception.Status = AttendanceExceptionStatus.Closed;
            exception.ResolutionMethod = AttendanceExceptionResolutionMethod.Correction;
            exception.ResolvedAt = now;
            _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), exception.Id, "PendingCorrection -> Closed: the correction was applied.", actor);
        }

        _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceApproval), approval.Id, $"{decision} by {role}; the correction is {approval.Status}.", actor);
        await _db.SaveChangesAsync(cancellationToken);

        await _engine.ProcessEmployeeDayAsync(approval.EmployeeId, approval.AttendanceDate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ApprovalResult.Correction(approval, exceptions.FirstOrDefault()?.Status ?? AttendanceExceptionStatus.Closed);
    }

    private async Task<ApprovalResult> DecideReviewAsync(AttendanceApproval approval, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken)
    {
        var record = await _db.AttendanceRecords.SingleAsync(r => r.Id == approval.AttendanceRecordId, cancellationToken);
        if (!record.IsCurrent || approval.Status == AttendanceApprovalStatus.Superseded)
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.Approval, "This version of the record was replaced by a recalculation. Review the current one.");
        }

        if (approval.Status != AttendanceApprovalStatus.Pending || record.Status != AttendanceRecordStatus.PendingReview)
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.Approval, $"The record is {record.Status}, not waiting for review.");
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (!approved && trimmed is null)
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.Reason, "A reason is required to reject a record.");
        }

        if (trimmed is { Length: > ReasonMaxLength })
        {
            return ApprovalResult.Invalid(ApprovalErrorKeys.Reason, $"The reason can be at most {ReasonMaxLength} characters.");
        }

        var employee = await _db.Profiles.AsNoTracking().SingleAsync(p => p.Id == record.EmployeeId, cancellationToken);
        if (!await _db.Profiles.AnyAsync(p => p.Id == decidedByEmployeeId, cancellationToken))
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (decidedByEmployeeId == record.EmployeeId)
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "A record can't be reviewed by its own employee.");
        }

        // A manager reviews their own direct reports; HR reviews anyone.
        if (!asHr && employee.ManagerId != decidedByEmployeeId)
        {
            return ApprovalResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager, or HR, can review this record.");
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
            return ApprovalResult.Invalid(ApprovalErrorKeys.Approval, "An exception of this shift is still open. Settle it first.");
        }

        var role = asHr ? "HR" : "Manager";
        var outcome = approved ? AttendanceApprovalStatus.Approved : AttendanceApprovalStatus.Rejected;
        var decision = approved ? AttendanceCorrectionDecision.Approved : AttendanceCorrectionDecision.Rejected;
        var now = _clock.GetLocalNow().DateTime;

        // The decision, the status, the audit entry and the day's summary are one unit.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        if (asHr)
        {
            (approval.HrDecision, approval.HrDecidedById, approval.HrDecidedAt) = (decision, decidedByEmployeeId, now);
        }
        else
        {
            (approval.ManagerDecision, approval.ManagerDecidedById, approval.ManagerDecidedAt) = (decision, decidedByEmployeeId, now);
        }

        approval.Status = outcome;
        approval.Reason = trimmed;
        approval.OvertimeMinutes = record.OvertimeMinutes;
        record.Status = approved ? AttendanceRecordStatus.Approved : AttendanceRecordStatus.Rejected;
        _audit.Record(
            AttendanceAuditActions.ReviewDecided,
            nameof(AttendanceRecord),
            record.Id,
            $"{outcome} by {role}; overtime {record.OvertimeMinutes} min.{(trimmed is null ? string.Empty : " Reason: " + trimmed)}",
            ActorOf(decidedByEmployeeId));
        await _db.SaveChangesAsync(cancellationToken);

        await _engine.ProcessEmployeeDayAsync(record.EmployeeId, record.AttendanceDate, cancellationToken);

        // If the day changed since the reviewer looked (a late punch), the recalculation wrote a new
        // version and this decision would cover a record nobody reads. Keep nothing; ask again.
        if (!await _db.AttendanceRecords.AsNoTracking().AnyAsync(r => r.Id == record.Id && r.IsCurrent, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return ApprovalResult.Invalid(ApprovalErrorKeys.Approval, "The day changed while it was being reviewed. Review it again.");
        }

        await transaction.CommitAsync(cancellationToken);
        return ApprovalResult.Review(approval, record.Status);
    }
}
