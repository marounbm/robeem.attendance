using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceCorrectionService : IAttendanceCorrectionService
{
    private const int JustificationMaxLength = 1000;

    private readonly IRobeemDbContext _db;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IAttendanceProcessingEngine _engine;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public AttendanceCorrectionService(IRobeemDbContext db, IAttendancePolicyResolver policies, IAttendanceProcessingEngine engine, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _policies = policies;
        _engine = engine;
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
        var correction = new AttendanceCorrection
        {
            AttendanceExceptionId = exception.Id,
            EmployeeId = exception.EmployeeId,
            AttendanceDate = exception.AttendanceDate,
            CorrectedEventType = missing,
            ProposedValue = proposed,
            TimeZoneId = timeZoneId,
            Justification = trimmed,
            RequestedByEmployeeId = requestedByEmployeeId,
            RequestedAt = _clock.GetLocalNow().DateTime,
            ApprovalWorkflow = policy.CorrectionApprovalWorkflow,
            Status = AttendanceCorrectionStatus.Pending,
        };
        _db.AttendanceCorrections.Add(correction);
        exception.Status = AttendanceExceptionStatus.PendingCorrection;
        _audit.Record(
            AttendanceAuditActions.ExceptionStatusChanged,
            nameof(AttendanceException),
            exception.Id,
            "Open -> PendingCorrection: a correction was proposed.",
            ActorOf(requestedByEmployeeId));
        await _db.SaveChangesAsync(cancellationToken);

        return CorrectionResult.Success(correction, exception.Status);
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
        var correction = new AttendanceCorrection
        {
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
            RequestedAt = _clock.GetLocalNow().DateTime,
            ApprovalWorkflow = policy.CorrectionApprovalWorkflow,
            Status = AttendanceCorrectionStatus.Pending,
        };
        _db.AttendanceCorrections.Add(correction);
        exception.Status = AttendanceExceptionStatus.PendingCorrection;
        _audit.Record(
            AttendanceAuditActions.ExceptionStatusChanged,
            nameof(AttendanceException),
            exception.Id,
            $"Open -> PendingCorrection: a change of the punch from {punch.NormalizedEventType} to {correction.CorrectedEventType} was proposed.",
            actor);
        await _db.SaveChangesAsync(cancellationToken);

        return CorrectionResult.Success(correction, exception.Status);
    }

    public async Task<CorrectionResult> DecideCorrectionAsync(int correctionId, int decidedByUserId, bool approved, bool isHrDecision, CancellationToken cancellationToken)
    {
        var correction = await _db.AttendanceCorrections
            .Include(c => c.AttendanceException)
            .SingleOrDefaultAsync(c => c.Id == correctionId, cancellationToken);
        if (correction?.AttendanceException is not { } exception)
        {
            return CorrectionResult.Missing();
        }

        if (correction.Status != AttendanceCorrectionStatus.Pending)
        {
            return CorrectionResult.Invalid(nameof(correctionId), $"The correction is {correction.Status}, not waiting for a decision.");
        }

        // The decider is the authenticated user, and the endpoint has checked that they hold the
        // role they decide in; here only the correction's own rules remain.
        if (!await _db.Profiles.AnyAsync(p => p.Id == decidedByUserId, cancellationToken))
        {
            return CorrectionResult.Invalid(CorrectionErrorKeys.DecidedBy, "The user does not exist.");
        }

        if (decidedByUserId == correction.RequestedByEmployeeId)
        {
            return CorrectionResult.Invalid(CorrectionErrorKeys.DecidedBy, "A correction can't be decided by the person who proposed it.");
        }

        // Who may take the manager's step: the employee's own manager. HR takes it only when the
        // employee has no manager, or an inactive one, so that no correction can be left undecidable.
        var employee = await _db.Profiles.AsNoTracking().Include(p => p.Manager).SingleAsync(p => p.Id == correction.EmployeeId, cancellationToken);
        var awaitingManagerStep = correction.ApprovalWorkflow != CorrectionApprovalWorkflow.HR && correction.ManagerDecision is null;
        var standsIn = isHrDecision && awaitingManagerStep && IsUnmanaged(employee);
        var decidesAsHr = isHrDecision && !standsIn;

        if (RoleProblem(correction, decidesAsHr) is { } problem)
        {
            return CorrectionResult.Invalid(nameof(DecideCorrectionRequest.IsHrDecision), problem);
        }

        if (!decidesAsHr && !standsIn && employee.ManagerId != decidedByUserId)
        {
            return CorrectionResult.Invalid(CorrectionErrorKeys.DecidedBy, "Only the employee's manager can decide this correction.");
        }

        var now = _clock.GetLocalNow().DateTime;
        var decision = approved ? AttendanceCorrectionDecision.Approved : AttendanceCorrectionDecision.Rejected;
        var actor = ActorOf(decidedByUserId);
        var role = decidesAsHr ? "HR" : standsIn ? "HR, in place of the manager" : "Manager";
        if (decidesAsHr)
        {
            (correction.HrDecision, correction.HrDecidedById, correction.HrDecidedAt) = (decision, decidedByUserId, now);
        }
        else
        {
            (correction.ManagerDecision, correction.ManagerDecidedById, correction.ManagerDecidedAt) = (decision, decidedByUserId, now);
        }

        if (!approved)
        {
            // Back to Open, so a new correction can be proposed (study §11.1).
            correction.Status = AttendanceCorrectionStatus.Rejected;
            exception.Status = AttendanceExceptionStatus.Open;
            _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceCorrection), correction.Id, $"{decision} by {role}; the correction is {correction.Status}.", actor);
            _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), exception.Id, "PendingCorrection -> Open: the correction was rejected.", actor);
            await _db.SaveChangesAsync(cancellationToken);
            return CorrectionResult.Success(correction, exception.Status);
        }

        // Every approval the workflow asks for is in once the last role of the workflow has approved:
        // the manager for Manager, HR for HR and for Both (the manager decides first).
        var complete = correction.ApprovalWorkflow == CorrectionApprovalWorkflow.Manager ? !decidesAsHr : decidesAsHr;
        if (!complete)
        {
            _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceCorrection), correction.Id, $"{decision} by {role}; the correction is still {correction.Status}.", actor);
            await _db.SaveChangesAsync(cancellationToken);
            return CorrectionResult.Success(correction, exception.Status);
        }

        // Approve, apply, close the exception and recalculate the day as one unit: a day recalculated
        // with the exception still open would never come out Approved, and a failure must not leave
        // the correction applied to a day that was not recalculated.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        correction.Status = AttendanceCorrectionStatus.Applied;
        correction.AppliedAt = now;
        exception.Status = AttendanceExceptionStatus.Closed;
        exception.ResolutionMethod = AttendanceExceptionResolutionMethod.Correction;
        exception.ResolvedAt = now;
        _audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceCorrection), correction.Id, $"{decision} by {role}; the correction is {correction.Status}.", actor);
        _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), exception.Id, "PendingCorrection -> Closed: the correction was applied.", actor);
        await _db.SaveChangesAsync(cancellationToken);

        await _engine.ProcessEmployeeDayAsync(correction.EmployeeId, correction.AttendanceDate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CorrectionResult.Success(correction, exception.Status);
    }

    public async Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, int approverEmployeeId, CancellationToken cancellationToken)
    {
        var pending = _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.Status == AttendanceCorrectionStatus.Pending);

        // The manager is asked first; HR next, or alone when the policy asks for HR only. A manager
        // sees their direct reports' corrections. HR also sees the manager-step corrections of
        // employees with no manager (or an inactive one): nobody else can decide them.
        pending = approver == CorrectionApprover.Manager
            ? pending.Where(c => c.ManagerDecision == null
                && (c.ApprovalWorkflow == CorrectionApprovalWorkflow.Manager || c.ApprovalWorkflow == CorrectionApprovalWorkflow.Both)
                && c.Employee!.ManagerId == approverEmployeeId)
            : pending.Where(c => c.ApprovalWorkflow == CorrectionApprovalWorkflow.HR
                || (c.ApprovalWorkflow == CorrectionApprovalWorkflow.Both && c.ManagerDecision == AttendanceCorrectionDecision.Approved)
                || (c.ManagerDecision == null
                    && c.ApprovalWorkflow != CorrectionApprovalWorkflow.HR
                    && (c.Employee!.ManagerId == null || c.Employee.Manager!.IsActive == false)));

        var rows = await pending
            .OrderBy(c => c.RequestedAt)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.AttendanceExceptionId,
                c.EmployeeId,
                c.Employee!.FirstName,
                c.Employee.LastName,
                c.AttendanceDate,
                c.Kind,
                c.OriginalEventType,
                c.CorrectedEventType,
                c.ProposedValue,
                c.Justification,
                c.RequestedAt,
                c.ApprovalWorkflow,
                c.ManagerDecision,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CorrectionListItem
            {
                Id = r.Id,
                ExceptionId = r.AttendanceExceptionId,
                EmployeeId = r.EmployeeId,
                EmployeeName = $"{r.FirstName} {r.LastName}",
                AttendanceDate = r.AttendanceDate,
                Kind = r.Kind.ToString(),
                OriginalEventType = r.OriginalEventType?.ToString(),
                CorrectedEventType = r.CorrectedEventType.ToString(),
                ProposedValue = r.ProposedValue,
                Justification = r.Justification,
                RequestedAt = r.RequestedAt,
                ApprovalWorkflow = r.ApprovalWorkflow.ToString(),
                ManagerDecision = r.ManagerDecision?.ToString(),
            })
            .ToList();
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

    /// <summary>An employee with no manager, or whose manager is inactive: HR decides for them.</summary>
    private static bool IsUnmanaged(Profile employee) => employee.ManagerId is null || employee.Manager is not { IsActive: true };

    /// <summary>Why this role can't decide the correction now, or null when it can.</summary>
    private static string? RoleProblem(AttendanceCorrection correction, bool isHrDecision) => correction.ApprovalWorkflow switch
    {
        CorrectionApprovalWorkflow.Manager when isHrDecision => "This correction needs the manager's decision only.",
        CorrectionApprovalWorkflow.HR when !isHrDecision => "This correction needs HR's decision only.",
        CorrectionApprovalWorkflow.Both when isHrDecision && correction.ManagerDecision is null => "The manager decides first, then HR.",
        CorrectionApprovalWorkflow.Both when !isHrDecision && correction.ManagerDecision is not null => "The manager has already decided.",
        _ => null,
    };

    /// <summary>
    /// The zone the proposed local time is in: that of the employee's punch nearest to it, as that
    /// device's clock is the one the schedule is compared with; the server's zone when the employee
    /// has no punch around then.
    /// </summary>
    private async Task<string> ResolveTimeZoneAsync(int employeeId, DateTime proposed, string actor, CancellationToken cancellationToken)
    {
        // A direct read of normalized attendance outside the engine: audited, and saved at once, since
        // a proposal that is then refused saves nothing else.
        _audit.Record(AttendanceAuditActions.AttendanceDataRead, nameof(NormalizedAttendanceTransaction), null, $"Time zone for a correction of employee {employeeId}, near {proposed:s}.", actor);
        await _db.SaveChangesAsync(cancellationToken);

        var nearby = await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.EmployeeId == employeeId
                && n.EventTimestampLocal >= proposed.AddDays(-1)
                && n.EventTimestampLocal < proposed.AddDays(1))
            .Select(n => new { n.EventTimestampLocal, n.TimeZoneId })
            .ToListAsync(cancellationToken);

        return nearby.Count == 0
            ? _clock.LocalTimeZone.Id
            : nearby.MinBy(n => (n.EventTimestampLocal - proposed).Duration())!.TimeZoneId;
    }
}
