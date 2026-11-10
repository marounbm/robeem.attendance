using Microsoft.EntityFrameworkCore;
using robeem.Application.HR.Absence;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceProcessingEngine : IAttendanceProcessingEngine
{
    // How far before its start and after its end a shift claims punches. When a neighboring shift
    // is closer, the midpoint between the two is the boundary instead.
    private static readonly TimeSpan EarlyArrivalWindow = TimeSpan.FromHours(4);
    private static readonly TimeSpan LateDepartureWindow = TimeSpan.FromHours(8);

    // The types an out-of-order punch can be changed to.
    private static readonly NormalizedEventType[] AttendanceTypes =
    [
        NormalizedEventType.CheckIn,
        NormalizedEventType.BreakOut,
        NormalizedEventType.BreakIn,
        NormalizedEventType.CheckOut,
    ];

    private readonly IRobeemDbContext _db;
    private readonly IScheduleResolutionService _schedules;
    private readonly IAttendancePolicyResolver _policies;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;
    private readonly IEmployeeAbsenceCalendar _absences;
    private readonly IRuleSetProvider _ruleSets;

    public AttendanceProcessingEngine(IRobeemDbContext db, IScheduleResolutionService schedules, IAttendancePolicyResolver policies, IAttendanceAuditTrail audit, TimeProvider clock, IEmployeeAbsenceCalendar absences, IRuleSetProvider ruleSets)
    {
        _db = db;
        _schedules = schedules;
        _policies = policies;
        _audit = audit;
        _clock = clock;
        _absences = absences;
        _ruleSets = ruleSets;
    }

    public async Task<DayProcessingResult> ProcessEmployeeDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        // a. The day's shifts, with the neighboring days' shifts, so that each shift claims its own
        // punches. An overnight shift claims the next morning's: its record is dated on its start.
        var resolutions = await _schedules.ResolveShiftsInRangeAsync(employeeId, day.AddDays(-1), day.AddDays(1), cancellationToken);
        var windows = BuildWindows(resolutions);

        // b. The employee's punches around the day: Unique only (CLAUDE.md §1.4).
        var punches = await LoadPunchesAsync(employeeId, day.AddDays(-1), day.AddDays(3), cancellationToken);

        // Applied corrections supply the punches that were missing (CLAUDE.md §1.7): additive, the
        // normalized rows stay as they are.
        punches.AddRange(await LoadCorrectedPunchesAsync(employeeId, day.AddDays(-1), day.AddDays(3), cancellationToken));

        // An applied change of type makes a punch the type the employee proposed and the approver agreed: the normalized row stays as it is.
        punches = await ApplyTypeChangesAsync(employeeId, punches, cancellationToken);

        // An applied "ignore" leaves a punch out altogether (a duplicate, a tap by mistake): the punch row stays, it just does not count.
        punches = await WithoutIgnoredPunchesAsync(employeeId, punches, cancellationToken);

        // c. The policy: the employee's schedule's, else the tenant default (CLAUDE.md §3).
        var policy = await _policies.ResolveAsync(employeeId, cancellationToken);

        // The rules that read breaks and decide which findings become exceptions: the policy scope's published set, else the policy's own settings.
        var rules = await _ruleSets.ResolveAsync(policy, cancellationToken);

        // d. One calculation per shift starting that day, plus one for punches no shift claims.
        var inputs = windows
            .Where(w => w.Date == day)
            .Select(w => (Shift: (ResolvedShift?)w.Shift, Punches: punches.Where(w.Contains).ToList()))
            .ToList();

        // Hourly staff have no shift: the hours worked are all there is. A check-in is paired with the check-out that follows it within
        // the policy's span, even after midnight, and the session is the day of its check-in.
        var isHourly = await _db.Profiles.AsNoTracking().AnyAsync(p => p.Id == employeeId && p.PayBasis == PayBasis.Hourly, cancellationToken);
        var unscheduled = isHourly
            ? HourlyDayPunches(punches, day, policy.HourlyMaxSpanHours)
            : punches.Where(p => p.Local.Date == day && !windows.Any(w => w.Contains(p))).ToList();
        if (unscheduled.Count > 0)
        {
            inputs.Add((null, unscheduled));
        }

        // Approved leave: the same range the shifts were resolved over, so leave after midnight meets an overnight shift.
        var calendar = await _absences.GetAsync([employeeId], day.AddDays(-1), day.AddDays(1), cancellationToken);
        var excused = calendar.For(employeeId);

        // A leave or holiday that covers the whole day: nothing was expected, so a shift nobody punched for is not a no-show and has
        // no record. A shift that was worked is calculated as usual. The day a shift starts on decides, as its record does.
        var wholeDay = excused.FirstOrDefault(p => p.IsWholeDay && p.Date == day);
        var excuseNote = wholeDay is not null && inputs.All(i => i.Punches.Count == 0) ? $"{wholeDay.Kind}: {wholeDay.Reference}" : null;
        if (wholeDay is not null)
        {
            inputs = inputs.Where(i => i.Punches.Count > 0).ToList();
        }

        var calculated = inputs
            .Select(i => (i.Shift, Calculation: ShiftAttendanceCalculator.Calculate(i.Shift, i.Punches, policy, excused, rules)))
            .ToList();

        // e. Exceptions, after the pairing: raised once, and closed when the problem is gone.
        var now = _clock.GetLocalNow().DateTime;
        var detected = new List<(int? ShiftId, DetectedException Detected)>();
        for (var i = 0; i < calculated.Count; i++)
        {
            var found = AttendanceExceptionDetector.Detect(calculated[i].Shift, day, calculated[i].Calculation, policy, now, rules);
            detected.AddRange(found.Select(d => (ShiftId: calculated[i].Shift?.TimeScheduleShiftId, Detected: WithSuggestions(d, found, inputs[i].Shift, inputs[i].Punches, day, policy, now, excused, rules))));
        }

        var exceptions = await ReconcileExceptionsAsync(employeeId, day, detected, now, excuseNote, cancellationToken);

        // f. Records, versioned. A shift with an exception that isn't closed is never auto-Approved.
        var (current, result) = await WriteRecordsAsync(employeeId, day, calculated, policy, exceptions.ShiftsWithActive, isHourly, rules.Version, cancellationToken);

        // g. The day summary.
        await WriteSummaryAsync(employeeId, day, current, exceptions.ActiveCount, cancellationToken);

        // All of the day's changes in one save: one transaction.
        await _db.SaveChangesAsync(cancellationToken);

        // The raised exceptions have ids now.
        await _audit.FlushNewAsync(cancellationToken);
        return result with { ExceptionsRaised = exceptions.Raised, ExceptionsResolved = exceptions.Resolved };
    }

    private static List<ShiftWindow> BuildWindows(IReadOnlyList<ShiftResolution> resolutions)
    {
        var shifts = resolutions
            .SelectMany(r => r.Shifts.Select(s => (r.Date, Shift: s)))
            .OrderBy(x => x.Shift.StartsAt)
            .ToList();

        var windows = new List<ShiftWindow>(shifts.Count);
        for (var i = 0; i < shifts.Count; i++)
        {
            var (date, shift) = shifts[i];
            var from = shift.StartsAt - EarlyArrivalWindow;
            var to = shift.EndsAt + LateDepartureWindow;
            if (i > 0)
            {
                from = Max(from, Midpoint(shifts[i - 1].Shift.EndsAt, shift.StartsAt));
            }

            if (i < shifts.Count - 1)
            {
                to = Min(to, Midpoint(shift.EndsAt, shifts[i + 1].Shift.StartsAt));
            }

            windows.Add(new ShiftWindow(date, shift, from, to));
        }

        return windows;
    }

    private static DateTime Midpoint(DateTime a, DateTime b) => a + ((b - a) / 2);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>
    /// The punches of the sessions that start on <paramref name="day"/>, for an employee paid by the hour. A session opens with the first
    /// punch, takes the punches that follow within <paramref name="maxSpanHours"/> of it, and closes at a check-out; a later punch opens the next.
    /// </summary>
    private static List<AttendancePunch> HourlyDayPunches(List<AttendancePunch> punches, DateTime day, int maxSpanHours)
    {
        var span = TimeSpan.FromHours(Math.Max(1, maxSpanHours));
        var result = new List<AttendancePunch>();
        DateTimeOffset? opened = null;
        var closed = true;
        var sessionIsDay = false;
        foreach (var punch in punches.OrderBy(p => p.Utc).ThenBy(p => p.NormalizedTransactionId))
        {
            if (opened is null || closed || punch.Utc - opened.Value > span)
            {
                (opened, closed, sessionIsDay) = (punch.Utc, false, punch.Local.Date == day);
            }

            if (punch.Type == NormalizedEventType.CheckOut)
            {
                closed = true;
            }

            if (sessionIsDay)
            {
                result.Add(punch);
            }
        }

        return result;
    }

    private static AttendanceRecord BuildRecord(int employeeId, DateTime day, ResolvedShift? shift, ShiftCalculation calculation, AttendancePolicy policy, bool hasActiveException, bool isHourly)
    {
        // Auto-approved only when complete, scheduled, free of overtime that needs pre-approval, and
        // free of exceptions that aren't closed (CLAUDE.md §1.5, study §11.3). Hourly staff have no shift: work outside one is normal.
        var needsReview = (shift is null && !isHourly)
            || hasActiveException
            || !calculation.IsComplete
            || (calculation.OvertimeMinutes > 0 && policy.RequiresOvertimePreApproval);

        return new AttendanceRecord
        {
            EmployeeId = employeeId,
            AttendanceDate = day,
            TimeScheduleShiftId = shift?.TimeScheduleShiftId,
            ScheduledStart = shift?.StartsAt,
            ScheduledEnd = shift?.EndsAt,
            ActualCheckIn = calculation.CheckIn?.Local,
            ActualCheckInSource = calculation.CheckIn?.Source,
            ActualCheckOut = calculation.CheckOut?.Local,
            ActualCheckOutSource = calculation.CheckOut?.Source,
            ActualBreakOut = calculation.BreakOut?.Local,
            ActualBreakOutSource = calculation.BreakOut?.Source,
            ActualBreakIn = calculation.BreakIn?.Local,
            ActualBreakInSource = calculation.BreakIn?.Source,
            WorkingHours = calculation.WorkingHours,
            BreakDuration = calculation.BreakDuration,
            LateMinutes = calculation.LateMinutes,
            EarlyDepartureMinutes = calculation.EarlyDepartureMinutes,
            OvertimeMinutes = calculation.OvertimeMinutes,
            ExcusedMinutes = calculation.ExcusedMinutes,
            BreakFindings = calculation.BreakFindings,
            Status = needsReview ? AttendanceRecordStatus.PendingReview : AttendanceRecordStatus.Approved,
            SourceNormalizedTransactionIds = calculation.SourceIds.Count == 0 ? null : string.Join(",", calculation.SourceIds),
            SourceCorrectionIds = calculation.CorrectionIds.Count == 0 ? null : string.Join(",", calculation.CorrectionIds),
        };
    }

    /// <summary>Whether two records hold the same calculation. The review status is left out, so a reviewer's decision survives an unchanged recalculation.</summary>
    private static bool SameCalculation(AttendanceRecord a, AttendanceRecord b) =>
        a.TimeScheduleShiftId == b.TimeScheduleShiftId
        && a.ScheduledStart == b.ScheduledStart
        && a.ScheduledEnd == b.ScheduledEnd
        && a.ActualCheckIn == b.ActualCheckIn
        && a.ActualCheckInSource == b.ActualCheckInSource
        && a.ActualCheckOut == b.ActualCheckOut
        && a.ActualCheckOutSource == b.ActualCheckOutSource
        && a.ActualBreakOut == b.ActualBreakOut
        && a.ActualBreakOutSource == b.ActualBreakOutSource
        && a.ActualBreakIn == b.ActualBreakIn
        && a.ActualBreakInSource == b.ActualBreakInSource
        && a.WorkingHours == b.WorkingHours
        && a.BreakDuration == b.BreakDuration
        && a.LateMinutes == b.LateMinutes
        && a.EarlyDepartureMinutes == b.EarlyDepartureMinutes
        && a.OvertimeMinutes == b.OvertimeMinutes
        && a.ExcusedMinutes == b.ExcusedMinutes
        && a.BreakFindings == b.BreakFindings
        && a.SourceNormalizedTransactionIds == b.SourceNormalizedTransactionIds
        && a.SourceCorrectionIds == b.SourceCorrectionIds;

    private static AttendanceRecordStatus RollUp(IReadOnlyList<AttendanceRecord> records) =>
        records.Count == 0 ? AttendanceRecordStatus.Calculated
        : records.Any(r => r.Status == AttendanceRecordStatus.Rejected) ? AttendanceRecordStatus.Rejected
        : records.All(r => r.Status == AttendanceRecordStatus.Approved) ? AttendanceRecordStatus.Approved
        : AttendanceRecordStatus.PendingReview;

    private static string SuggestedNames(DetectedException found) =>
        found.SuggestedTypes is { Count: > 0 } types ? string.Join(",", types.Select(t => t.ToString())) : string.Empty;

    /// <summary>
    /// For an invalid sequence, finds the types its out-of-order punch could be given so that the exception goes away without making
    /// another punch out of order: each type is tried by calculating the shift again with the punch changed. What a type leaves
    /// undone (a check-out still missing, say) is a missing punch, which is another exception and handled as one.
    /// </summary>
    private static DetectedException WithSuggestions(
        DetectedException found,
        IReadOnlyList<DetectedException> allFound,
        ResolvedShift? shift,
        List<AttendancePunch> punches,
        DateTime day,
        AttendancePolicy policy,
        DateTime now,
        IReadOnlyList<ExcusedPeriod> excused,
        RuleSetSnapshot rules)
    {
        if (found.Type != AttendanceExceptionType.InvalidSequence || found.NormalizedTransactionId is not { } transactionId)
        {
            return found;
        }

        var offending = punches.FirstOrDefault(p => p.NormalizedTransactionId == transactionId);
        if (offending is null)
        {
            return found;
        }

        var invalidNow = allFound.Count(d => d.Type == AttendanceExceptionType.InvalidSequence);
        var suggested = new List<NormalizedEventType>();
        foreach (var type in AttendanceTypes.Where(t => t != offending.Type))
        {
            var changed = punches.Select(p => p.NormalizedTransactionId == transactionId ? p with { Type = type } : p).ToList();
            var detected = AttendanceExceptionDetector.Detect(shift, day, ShiftAttendanceCalculator.Calculate(shift, changed, policy, excused, rules), policy, now, rules);
            var invalid = detected.Where(d => d.Type == AttendanceExceptionType.InvalidSequence).ToList();
            if (invalid.All(d => d.NormalizedTransactionId != transactionId) && invalid.Count < invalidNow)
            {
                suggested.Add(type);
            }
        }

        return found with { SuggestedTypes = suggested, PunchType = offending.Type, PunchAt = offending.Local };
    }

    private async Task<List<AttendancePunch>> LoadPunchesAsync(int employeeId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        // Rows of an unknown device are left out: their local time is UTC, not the device's (P5),
        // so comparing them with the schedule would be wrong. A person resolves them first.
        var rows = await _db.NormalizedAttendanceTransactions
            .AsNoTracking()
            .Where(n => n.EmployeeId == employeeId
                && n.DuplicateStatus == DuplicateStatus.Unique
                && n.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded
                && n.ExceptionStatus != TransactionExceptionStatus.FlaggedUnrecognizedDevice
                && n.EventTimestampLocal >= from
                && n.EventTimestampLocal < to)
            .Select(n => new { n.Id, n.NormalizedEventType, n.EventTimestampUtc, n.EventTimestampLocal })
            .ToListAsync(cancellationToken);

        return rows
            .Select(n => new AttendancePunch(n.Id, n.NormalizedEventType, n.EventTimestampUtc, n.EventTimestampLocal, AttendancePunchSource.Normalized))
            .ToList();
    }

    private async Task<List<AttendancePunch>> WithoutIgnoredPunchesAsync(int employeeId, List<AttendancePunch> punches, CancellationToken cancellationToken)
    {
        var ignored = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.AttendanceApproval!.Status == AttendanceApprovalStatus.Applied
                && c.Kind == AttendanceCorrectionKind.IgnorePunch
                && c.NormalizedTransactionId != null)
            .Select(c => c.NormalizedTransactionId!.Value)
            .ToListAsync(cancellationToken);

        return ignored.Count == 0
            ? punches
            : punches.Where(p => p.NormalizedTransactionId is not { } id || !ignored.Contains(id)).ToList();
    }

    private async Task<List<AttendancePunch>> ApplyTypeChangesAsync(int employeeId, List<AttendancePunch> punches, CancellationToken cancellationToken)
    {
        var changes = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.AttendanceApproval!.Status == AttendanceApprovalStatus.Applied
                && c.Kind == AttendanceCorrectionKind.ChangePunchType
                && c.NormalizedTransactionId != null)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, TransactionId = c.NormalizedTransactionId!.Value, c.CorrectedEventType })
            .ToListAsync(cancellationToken);
        if (changes.Count == 0)
        {
            return punches;
        }

        // The latest applied change of a punch is the one that counts.
        var latest = changes.GroupBy(c => c.TransactionId).ToDictionary(g => g.Key, g => g.Last());
        return punches
            .Select(p => p.NormalizedTransactionId is { } id && latest.TryGetValue(id, out var change)
                ? p with { Type = change.CorrectedEventType, Source = AttendancePunchSource.Corrected, CorrectionId = change.Id }
                : p)
            .ToList();
    }

    private async Task<List<AttendancePunch>> LoadCorrectedPunchesAsync(int employeeId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var rows = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.Kind == AttendanceCorrectionKind.SupplyPunch
                && c.AttendanceApproval!.Status == AttendanceApprovalStatus.Applied
                && c.ProposedValue >= from
                && c.ProposedValue < to)
            .Select(c => new { c.Id, c.CorrectedEventType, c.ProposedValue, c.TimeZoneId })
            .ToListAsync(cancellationToken);

        // An added punch that a later, applied correction took back is not read.
        var cancelled = await _db.AttendanceCorrections
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.Kind == AttendanceCorrectionKind.CancelSuppliedPunch
                && c.AttendanceApproval!.Status == AttendanceApprovalStatus.Applied
                && c.CancelsCorrectionId != null)
            .Select(c => c.CancelsCorrectionId!.Value)
            .ToListAsync(cancellationToken);

        // The instant comes from the zone fixed when the correction was proposed, so it can't drift.
        return rows
            .Where(c => !cancelled.Contains(c.Id))
            .Select(c => new AttendancePunch(
                null,
                c.CorrectedEventType,
                new VendorTimestamp(c.ProposedValue, null).ToUtc(c.TimeZoneId) ?? new DateTimeOffset(c.ProposedValue, TimeSpan.Zero),
                c.ProposedValue,
                AttendancePunchSource.Corrected,
                c.Id))
            .ToList();
    }

    private async Task<(List<AttendanceRecord> Current, DayProcessingResult Result)> WriteRecordsAsync(
        int employeeId,
        DateTime day,
        List<(ResolvedShift? Shift, ShiftCalculation Calculation)> calculated,
        AttendancePolicy policy,
        HashSet<int?> shiftsWithActiveException,
        bool isHourly,
        int ruleSetVersion,
        CancellationToken cancellationToken)
    {
        var existing = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate == day)
            .ToListAsync(cancellationToken);

        // A record left for review has a pending review approval, the one place an approver finds it. The approvals follow the records:
        // queued when a record is left for review, set aside (Superseded) when its version is retired or the engine finds nothing left to review.
        var existingIds = existing.Select(r => r.Id).ToList();
        var pendingReviews = await _db.AttendanceApprovals
            .Where(a => a.Type == AttendanceApprovalType.Review
                && a.Status == AttendanceApprovalStatus.Pending
                && existingIds.Contains(a.AttendanceRecordId!.Value))
            .ToListAsync(cancellationToken);
        var now = _clock.GetLocalNow().DateTime;

        void SetAside(AttendanceRecord record) =>
            pendingReviews.Where(a => a.AttendanceRecordId == record.Id).ToList().ForEach(a => a.Status = AttendanceApprovalStatus.Superseded);

        void Queue(AttendanceRecord record)
        {
            if (!pendingReviews.Any(a => a.AttendanceRecordId == record.Id))
            {
                var queued = ApprovalRules.NewReviewApproval(record, now);
                _db.AttendanceApprovals.Add(queued);
                pendingReviews.Add(queued);
            }
        }

        var current = new List<AttendanceRecord>();
        int written = 0, unchanged = 0;
        foreach (var (shift, calculation) in calculated)
        {
            var hasActiveException = shiftsWithActiveException.Contains(shift?.TimeScheduleShiftId);
            var candidate = BuildRecord(employeeId, day, shift, calculation, policy, hasActiveException, isHourly);
            candidate.RuleSetVersion = ruleSetVersion;
            var versions = existing.Where(r => r.TimeScheduleShiftId == candidate.TimeScheduleShiftId).ToList();
            var previous = versions.Where(r => r.IsCurrent).OrderByDescending(r => r.Version).FirstOrDefault();

            if (previous is not null && SameCalculation(previous, candidate))
            {
                // The rules in force are not part of the calculation: a day that comes out the same keeps its record, and only notes which set it was last checked with.
                previous.RuleSetVersion = ruleSetVersion;

                // The status is a reviewer's to change, and is kept, except that an exception which
                // appeared after an approval takes the approval back.
                if (hasActiveException && previous.Status is AttendanceRecordStatus.Approved or AttendanceRecordStatus.Calculated)
                {
                    previous.Status = AttendanceRecordStatus.PendingReview;
                }
                else if (previous.Status == AttendanceRecordStatus.PendingReview && candidate.Status == AttendanceRecordStatus.Approved)
                {
                    // Nothing is left to review: the exception that held the day was accepted. A reviewer's own decision (approved or
                    // rejected) is never changed here.
                    previous.Status = AttendanceRecordStatus.Approved;
                    SetAside(previous);
                }

                if (previous.Status == AttendanceRecordStatus.PendingReview)
                {
                    Queue(previous);
                }

                current.Add(previous);
                unchanged++;
                continue;
            }

            // Never rewritten (study §6.3): a new version, and the previous one retired.
            candidate.Version = versions.Count == 0 ? 1 : versions.Max(r => r.Version) + 1;
            if (previous is not null)
            {
                previous.IsCurrent = false;
                previous.SupersededByRecord = candidate;
                SetAside(previous);
            }

            _db.AttendanceRecords.Add(candidate);
            if (candidate.Status == AttendanceRecordStatus.PendingReview)
            {
                Queue(candidate);
            }

            current.Add(candidate);
            written++;
        }

        // Current records of shifts no longer scheduled that day (or of unscheduled work that no
        // longer exists) are retired without a successor.
        var keys = calculated.Select(c => c.Shift?.TimeScheduleShiftId).ToHashSet();
        var stale = existing.Where(r => r.IsCurrent && !keys.Contains(r.TimeScheduleShiftId)).ToList();
        stale.ForEach(r =>
        {
            r.IsCurrent = false;
            SetAside(r);
        });

        return (current, new DayProcessingResult(written, unchanged, stale.Count));
    }

    private async Task WriteSummaryAsync(int employeeId, DateTime day, List<AttendanceRecord> current, int activeExceptionCount, CancellationToken cancellationToken)
    {
        var summary = await _db.AttendanceDaySummaries
            .SingleOrDefaultAsync(s => s.EmployeeId == employeeId && s.AttendanceDate == day, cancellationToken);
        if (summary is null)
        {
            if (current.Count == 0)
            {
                return;
            }

            summary = new AttendanceDaySummary { EmployeeId = employeeId, AttendanceDate = day };
            _db.AttendanceDaySummaries.Add(summary);
        }

        summary.ExceptionCount = activeExceptionCount;
        summary.TotalWorkingHours = current.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.WorkingHours);
        summary.TotalBreakHours = current.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.BreakDuration);
        summary.TotalOvertimeHours = TimeSpan.FromMinutes(current.Sum(r => r.OvertimeMinutes));
        summary.ShiftCount = current.Count;
        summary.Status = RollUp(current);
    }

    /// <summary>
    /// Brings the day's stored exceptions in line with the detected ones. A detected exception with
    /// no active match is opened; an active one that matches is kept as it is; an Open one that is
    /// no longer detected is closed as auto-resolved. A <c>PendingCorrection</c> one is left to its
    /// correction, whatever is detected.
    /// </summary>
    private async Task<ExceptionOutcome> ReconcileExceptionsAsync(
        int employeeId,
        DateTime day,
        List<(int? ShiftId, DetectedException Detected)> detected,
        DateTime now,
        string? excuseNote,
        CancellationToken cancellationToken)
    {
        var stored = await _db.AttendanceExceptions
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day && e.Status != AttendanceExceptionStatus.Closed)
            .ToListAsync(cancellationToken);

        // A break exception someone accepted stays accepted while the same break is found, so it is not raised again.
        var accepted = await _db.AttendanceExceptions
            .AsNoTracking()
            .Where(e => e.EmployeeId == employeeId
                && e.AttendanceDate == day
                && e.Status == AttendanceExceptionStatus.Closed
                && e.ResolutionMethod == AttendanceExceptionResolutionMethod.Accepted)
            .ToListAsync(cancellationToken);

        var active = new List<AttendanceException>();
        var matched = new HashSet<AttendanceException>();
        var raised = 0;
        foreach (var (shiftId, found) in detected)
        {
            if (accepted.Any(e => e.TimeScheduleShiftId == shiftId
                && e.ExceptionType == found.Type
                && e.RelatedNormalizedTransactionId == found.NormalizedTransactionId))
            {
                continue;
            }

            var match = stored.FirstOrDefault(e => !matched.Contains(e)
                && e.TimeScheduleShiftId == shiftId
                && e.ExceptionType == found.Type
                && e.MissingEventType == found.MissingEventType
                && e.RelatedNormalizedTransactionId == found.NormalizedTransactionId);
            if (match is not null)
            {
                matched.Add(match);
                active.Add(match);

                // What would settle it can change as the day's punches do.
                match.SuggestedEventTypes = SuggestedNames(found);
                (match.RelatedEventType, match.RelatedEventAt) = (found.PunchType, found.PunchAt);
                continue;
            }

            var opened = new AttendanceException
            {
                EmployeeId = employeeId,
                AttendanceDate = day,
                TimeScheduleShiftId = shiftId,
                ExceptionType = found.Type,
                MissingEventType = found.MissingEventType,
                RelatedNormalizedTransactionId = found.NormalizedTransactionId,
                SuggestedEventTypes = SuggestedNames(found),
                RelatedEventType = found.PunchType,
                RelatedEventAt = found.PunchAt,
                Status = AttendanceExceptionStatus.Open,
                Description = found.Description,
                DetectedAt = now,
            };
            _db.AttendanceExceptions.Add(opened);
            _audit.RecordNew(AttendanceAuditActions.ExceptionStatusChanged, opened, $"(new) -> Open: {found.Description}");
            active.Add(opened);
            raised++;
        }

        var resolved = 0;
        foreach (var gone in stored.Where(e => !matched.Contains(e)))
        {
            if (gone.Status == AttendanceExceptionStatus.Open && gone.ExceptionType == AttendanceExceptionType.NoShow && excuseNote is not null)
            {
                // A leave or holiday that was entered after the no-show was raised: nothing was expected of the employee.
                gone.Status = AttendanceExceptionStatus.Closed;
                gone.ResolutionMethod = AttendanceExceptionResolutionMethod.ExcusedAbsence;
                gone.ResolvedAt = now;
                gone.ResolutionNote = excuseNote;
                _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), gone.Id, $"Open -> Closed: excused by approved absence ({excuseNote}).");
                resolved++;
            }
            else if (gone.Status == AttendanceExceptionStatus.Open)
            {
                gone.Status = AttendanceExceptionStatus.Closed;
                gone.ResolutionMethod = AttendanceExceptionResolutionMethod.AutoResolved;
                gone.ResolvedAt = now;
                _audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), gone.Id, "Open -> Closed: the problem is gone (auto-resolved).");
                resolved++;
            }
            else
            {
                active.Add(gone);
            }
        }

        return new ExceptionOutcome(active.Select(e => e.TimeScheduleShiftId).ToHashSet(), active.Count, raised, resolved);
    }

    /// <summary>The punches a shift claims: local time in <c>[From, To)</c>.</summary>
    private sealed record ExceptionOutcome(HashSet<int?> ShiftsWithActive, int ActiveCount, int Raised, int Resolved);

    private sealed record ShiftWindow(DateTime Date, ResolvedShift Shift, DateTime From, DateTime To)
    {
        public bool Contains(AttendancePunch punch) => punch.Local >= From && punch.Local < To;
    }
}
