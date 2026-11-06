using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceCorrectionService : IAttendanceCorrectionService
{
    private const int JustificationMaxLength = 1000;
    private const int ListPageSize = 200;

    private readonly IRobeemDbContext _db;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IAttendanceApprovalService _approvals;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceCorrectionService(IRobeemDbContext db, IAttendancePolicyResolver policies, IAttendanceApprovalService approvals, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _policies = policies;
        _approvals = approvals;
        _audit = audit;
        _clock = clock;
    }

    public async Task<CorrectionResult> ProposeCorrectionAsync(int exceptionId, DateTime proposedValue, string justification, int requestedByEmployeeId, CancellationToken cancellationToken)
    {
        var exception = await _db.AttendanceExceptions.Include(e => e.TimeScheduleShift).SingleOrDefaultAsync(e => e.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return CorrectionResult.Missing();
        }

        var errors = new Dictionary<string, string>();
        var missing = exception.MissingEventType.GetValueOrDefault();
        if (exception.Status != AttendanceExceptionStatus.Open)
        {
            errors[nameof(ProposeCorrectionRequest.ExceptionId)] = exception.Status == AttendanceExceptionStatus.PendingCorrection
                ? "A correction for this exception is already waiting for a decision."
                : "This exception is closed.";
        }
        else if (exception.ExceptionType != AttendanceExceptionType.MissingPunch || exception.MissingEventType is null)
        {
            // A correction supplies a punch. An out-of-order punch can't be fixed by adding one.
            errors[nameof(ProposeCorrectionRequest.ExceptionId)] = "Only a missing punch can be corrected: a correction supplies the punch.";
        }
        else if (requestedByEmployeeId != exception.EmployeeId)
        {
            // The requester is the authenticated user: they must be the exception's employee.
            errors[CorrectionErrorKeys.RequestedBy] = "A correction can only be proposed by the employee the exception belongs to.";
        }

        var trimmed = ValidateJustification(justification, errors);

        var proposed = DateTime.SpecifyKind(proposedValue, DateTimeKind.Unspecified);
        var timeZoneId = string.Empty;
        var day = exception.AttendanceDate.Date;

        // The punch belongs to the exception's day. Only a shift that runs past midnight has punches on the day after.
        var overnight = exception.TimeScheduleShift is { } shift && shift.IsOvernight();
        if (proposed.Date != day && !(overnight && proposed.Date == day.AddDays(1)))
        {
            errors[nameof(ProposeCorrectionRequest.ProposedValue)] = overnight
                ? $"The time must be on {day:yyyy-MM-dd}, the day of the punch, or on the day after (the shift ends after midnight)."
                : $"The time must be on {day:yyyy-MM-dd}, the day of the punch.";
        }
        else
        {
            timeZoneId = await ResolveTimeZoneAsync(exception.EmployeeId, proposed, ActorOf(requestedByEmployeeId), cancellationToken);
            var instant = new VendorTimestamp(proposed, null).ToUtc(timeZoneId);
            if (instant is null)
            {
                errors[nameof(ProposeCorrectionRequest.ProposedValue)] = "That is not a valid local time.";
            }
            else if (instant > _clock.GetUtcNow())
            {
                errors[nameof(ProposeCorrectionRequest.ProposedValue)] = "The time can't be in the future.";
            }
        }

        if (errors.Count > 0)
        {
            return CorrectionResult.Invalid(errors);
        }

        var employeeExists = await _db.Profiles.AnyAsync(p => p.Id == requestedByEmployeeId, cancellationToken);
        if (!employeeExists)
        {
            return CorrectionResult.Invalid(CorrectionErrorKeys.RequestedBy, "The employee does not exist.");
        }

        var policy = await _policies.ResolveAsync(exception.EmployeeId, cancellationToken);
        var now = _clock.GetLocalNow().DateTime;
        var approval = ApprovalRules.NewCorrectionApproval(exception.EmployeeId, exception.AttendanceDate, requestedByEmployeeId, now, policy.CorrectionApprovalWorkflow);
        var correction = new AttendanceCorrection
        {
            AttendanceApproval = approval,
            AttendanceExceptionId = exception.Id,
            EmployeeId = exception.EmployeeId,
            AttendanceDate = exception.AttendanceDate,
            CorrectedEventType = missing,
            ProposedValue = proposed,
            TimeZoneId = timeZoneId,
            Justification = trimmed,
            RequestedByEmployeeId = requestedByEmployeeId,
            RequestedAt = now,
        };
        _db.AttendanceApprovals.Add(approval);
        _db.AttendanceCorrections.Add(correction);
        exception.Status = AttendanceExceptionStatus.PendingCorrection;
        _audit.Record(
            AttendanceAuditActions.ExceptionStatusChanged,
            nameof(AttendanceException),
            exception.Id,
            "Open -> PendingCorrection: a correction was proposed.",
            ActorOf(requestedByEmployeeId));
        await _db.SaveChangesAsync(cancellationToken);

        return CorrectionResult.Success(correction.Id, approval.Status, exception.Status);
    }

    public async Task<CorrectionResult> ProposeTypeChangeAsync(int exceptionId, string newEventType, string justification, int requestedByEmployeeId, CancellationToken cancellationToken)
    {
        var exception = await _db.AttendanceExceptions.SingleOrDefaultAsync(e => e.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return CorrectionResult.Missing();
        }

        var errors = new Dictionary<string, string>();
        var suggested = exception.SuggestedEventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (exception.Status != AttendanceExceptionStatus.Open)
        {
            errors[nameof(ProposeCorrectionRequest.ExceptionId)] = exception.Status == AttendanceExceptionStatus.PendingCorrection
                ? "A correction for this exception is already waiting for a decision."
                : "This exception is closed.";
        }
        else if (exception.ExceptionType != AttendanceExceptionType.InvalidSequence || exception.RelatedNormalizedTransactionId is null)
        {
            errors[nameof(ProposeCorrectionRequest.ExceptionId)] = "Only a punch that is out of order can be changed to another type. A missing punch is corrected with the time it happened.";
        }
        else if (requestedByEmployeeId != exception.EmployeeId)
        {
            errors[CorrectionErrorKeys.RequestedBy] = "A correction can only be proposed by the employee the exception belongs to.";
        }
        else if (!Enum.TryParse<NormalizedEventType>(newEventType, ignoreCase: true, out var chosen) || !suggested.Contains(chosen.ToString(), StringComparer.Ordinal))
        {
            errors[nameof(ProposeCorrectionRequest.NewEventType)] = suggested.Length == 0
                ? "No other type would settle this exception."
                : $"That type would not settle this exception. Choose one of: {string.Join(", ", suggested)}.";
        }

        var trimmed = ValidateJustification(justification, errors);
        if (errors.Count > 0)
        {
            return CorrectionResult.Invalid(errors);
        }

        // Reading the punch is a direct read of normalized attendance outside the engine: audited, and saved first.
        var actor = ActorOf(requestedByEmployeeId);
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), exception.RelatedNormalizedTransactionId, $"The out-of-order punch of exception {exception.Id}, for a change of type.", actor);
        await _db.SaveChangesAsync(cancellationToken);
        var punch = await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .SingleAsync(n => n.Id == exception.RelatedNormalizedTransactionId, cancellationToken);

        var policy = await _policies.ResolveAsync(exception.EmployeeId, cancellationToken);
        var now = _clock.GetLocalNow().DateTime;
        var approval = ApprovalRules.NewCorrectionApproval(exception.EmployeeId, exception.AttendanceDate, requestedByEmployeeId, now, policy.CorrectionApprovalWorkflow);
        var correction = new AttendanceCorrection
        {
            AttendanceApproval = approval,
            AttendanceExceptionId = exception.Id,
            EmployeeId = exception.EmployeeId,
            AttendanceDate = exception.AttendanceDate,
            Kind = AttendanceCorrectionKind.ChangePunchType,
            NormalizedTransactionId = punch.Id,
            OriginalEventType = punch.NormalizedEventType,
            CorrectedEventType = Enum.Parse<NormalizedEventType>(newEventType, ignoreCase: true),
            ProposedValue = punch.EventTimestampLocal,
            TimeZoneId = punch.TimeZoneId,
            Justification = trimmed,
            RequestedByEmployeeId = requestedByEmployeeId,
            RequestedAt = now,
        };
        _db.AttendanceApprovals.Add(approval);
        _db.AttendanceCorrections.Add(correction);
        exception.Status = AttendanceExceptionStatus.PendingCorrection;
        _audit.Record(
            AttendanceAuditActions.ExceptionStatusChanged,
            nameof(AttendanceException),
            exception.Id,
            $"Open -> PendingCorrection: a change of the punch from {punch.NormalizedEventType} to {correction.CorrectedEventType} was proposed.",
            actor);
        await _db.SaveChangesAsync(cancellationToken);

        return CorrectionResult.Success(correction.Id, approval.Status, exception.Status);
    }

    public async Task<CorrectionResult> DecideCorrectionAsync(int correctionId, int decidedByUserId, bool approved, bool isHrDecision, CancellationToken cancellationToken)
    {
        // The decision is the approval's: a correction row is only one of the edits of its request.
        var approvalId = await _db.AttendanceCorrections
            .Where(c => c.Id == correctionId)
            .Select(c => (int?)c.AttendanceApprovalId)
            .SingleOrDefaultAsync(cancellationToken);
        if (approvalId is null)
        {
            return CorrectionResult.Missing();
        }

        var result = await _approvals.DecideAsync(approvalId.Value, decidedByUserId, isHrDecision, approved, null, cancellationToken);
        if (result.NotFound)
        {
            return CorrectionResult.Missing();
        }

        return result.Succeeded
            ? CorrectionResult.Success(correctionId, result.Status!.Value, result.ExceptionStatus!.Value)
            : CorrectionResult.Invalid(result.Errors.ToDictionary(e => e.Key == ApprovalErrorKeys.Approval ? nameof(correctionId) : e.Key, e => e.Value));
    }

    public async Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, int approverEmployeeId, CancellationToken cancellationToken)
    {
        // The manager is asked first; HR next, or alone when the policy asks for HR only. A manager sees their direct reports'
        // corrections. HR also sees the manager-step corrections of employees with no manager (or an inactive one): nobody else can
        // decide them. One item per request; the approval service does the reading, a page at a time, until the queue is read.
        var isManager = approver == CorrectionApprover.Manager;
        var items = new List<CorrectionListItem>();
        for (var page = 1; ; page++)
        {
            var filter = new ApprovalFilter(null, null, null, AttendanceApprovalType.Correction, null, page, ListPageSize);
            var result = await _approvals.ListPendingAsync(approverEmployeeId, isManager, !isManager, filter, cancellationToken);
            items.AddRange(result.Items.Select(i => i.Correction!));
            if (result.Items.Count == 0 || items.Count >= result.Total)
            {
                return items;
            }
        }
    }

    private static string ActorOf(int employeeId) => $"employee:{employeeId}";

    /// <summary>Checks the justification a proposal needs, adding what is wrong with it to <paramref name="errors"/>; returns it trimmed.</summary>
    private static string ValidateJustification(string? justification, Dictionary<string, string> errors)
    {
        var trimmed = (justification ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            errors[nameof(ProposeCorrectionRequest.Justification)] = "A justification is required.";
        }
        else if (trimmed.Length > JustificationMaxLength)
        {
            errors[nameof(ProposeCorrectionRequest.Justification)] = $"The justification can be at most {JustificationMaxLength} characters.";
        }

        return trimmed;
    }

    /// <summary>
    /// The zone the proposed local time is in: that of the employee's punch nearest to it, as that
    /// device's clock is the one the schedule is compared with; the server's zone when the employee
    /// has no punch around then.
    /// </summary>
    private Task<string> ResolveTimeZoneAsync(int employeeId, DateTime proposed, string actor, CancellationToken cancellationToken) =>
        CorrectionTimeZones.ResolveAsync(_db, _audit, _clock, employeeId, proposed, actor, cancellationToken);
}
