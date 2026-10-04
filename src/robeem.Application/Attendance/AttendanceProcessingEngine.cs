using Microsoft.EntityFrameworkCore;
using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceProcessingEngine : IAttendanceProcessingEngine
{
    // How far before its start and after its end a shift claims punches. When a neighboring shift
    // is closer, the midpoint between the two is the boundary instead.
    private static readonly TimeSpan EarlyArrivalWindow = TimeSpan.FromHours(4);
    private static readonly TimeSpan LateDepartureWindow = TimeSpan.FromHours(8);

    private readonly IRobeemDbContext _db;
    private readonly IScheduleResolutionService _schedules;
    private readonly IAttendancePolicyResolver _policies;
    private readonly TimeProvider _clock;

    public AttendanceProcessingEngine(IRobeemDbContext db, IScheduleResolutionService schedules, IAttendancePolicyResolver policies, TimeProvider clock)
    {
        _db = db;
        _schedules = schedules;
        _policies = policies;
        _clock = clock;
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

        // c. The policy: the employee's schedule's, else the tenant default (CLAUDE.md §3).
        var policy = await _policies.ResolveAsync(employeeId, cancellationToken);

        // d. One calculation per shift starting that day, plus one for punches no shift claims.
        var calculated = windows
            .Where(w => w.Date == day)
            .Select(w => (Shift: (ResolvedShift?)w.Shift, Calculation: ShiftAttendanceCalculator.Calculate(w.Shift, punches.Where(w.Contains).ToList(), policy)))
            .ToList();
        var unscheduled = punches.Where(p => p.Local.Date == day && !windows.Any(w => w.Contains(p))).ToList();
        if (unscheduled.Count > 0)
        {
            calculated.Add((null, ShiftAttendanceCalculator.Calculate(null, unscheduled, policy)));
        }

        // e. Exceptions, after the pairing: raised once, and closed when the problem is gone.
        var now = _clock.GetLocalNow().DateTime;
        var detected = calculated
            .SelectMany(c => AttendanceExceptionDetector
                .Detect(c.Shift, day, c.Calculation, policy, now)
                .Select(d => (ShiftId: c.Shift?.TimeScheduleShiftId, Detected: d)))
            .ToList();
        var exceptions = await ReconcileExceptionsAsync(employeeId, day, detected, now, cancellationToken);

        // f. Records, versioned. A shift with an exception that isn't closed is never auto-Approved.
        var (current, result) = await WriteRecordsAsync(employeeId, day, calculated, policy, exceptions.ShiftsWithActive, cancellationToken);

        // g. The day summary.
        await WriteSummaryAsync(employeeId, day, current, exceptions.ActiveCount, cancellationToken);

        // All of the day's changes in one save: one transaction.
        await _db.SaveChangesAsync(cancellationToken);
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

    private static AttendanceRecord BuildRecord(int employeeId, DateTime day, ResolvedShift? shift, ShiftCalculation calculation, AttendancePolicy policy, bool hasActiveException)
    {
        // Auto-approved only when complete, scheduled, free of overtime that needs pre-approval, and
        // free of exceptions that aren't closed (CLAUDE.md §1.5, study §11.3).
        var needsReview = shift is null
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
            Status = needsReview ? AttendanceRecordStatus.PendingReview : AttendanceRecordStatus.Approved,
            SourceNormalizedTransactionIds = calculation.SourceIds.Count == 0 ? null : string.Join(",", calculation.SourceIds),
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
        && a.SourceNormalizedTransactionIds == b.SourceNormalizedTransactionIds;

    private static AttendanceRecordStatus RollUp(IReadOnlyList<AttendanceRecord> records) =>
        records.Count == 0 ? AttendanceRecordStatus.Calculated
        : records.Any(r => r.Status == AttendanceRecordStatus.Rejected) ? AttendanceRecordStatus.Rejected
        : records.All(r => r.Status == AttendanceRecordStatus.Approved) ? AttendanceRecordStatus.Approved
        : AttendanceRecordStatus.PendingReview;

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

    private async Task<(List<AttendanceRecord> Current, DayProcessingResult Result)> WriteRecordsAsync(
        int employeeId,
        DateTime day,
        List<(ResolvedShift? Shift, ShiftCalculation Calculation)> calculated,
        AttendancePolicy policy,
        HashSet<int?> shiftsWithActiveException,
        CancellationToken cancellationToken)
    {
        var existing = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate == day)
            .ToListAsync(cancellationToken);

        var current = new List<AttendanceRecord>();
        int written = 0, unchanged = 0;
        foreach (var (shift, calculation) in calculated)
        {
            var hasActiveException = shiftsWithActiveException.Contains(shift?.TimeScheduleShiftId);
            var candidate = BuildRecord(employeeId, day, shift, calculation, policy, hasActiveException);
            var versions = existing.Where(r => r.TimeScheduleShiftId == candidate.TimeScheduleShiftId).ToList();
            var previous = versions.Where(r => r.IsCurrent).OrderByDescending(r => r.Version).FirstOrDefault();

            if (previous is not null && SameCalculation(previous, candidate))
            {
                // The status is a reviewer's to change, and is kept, except that an exception which
                // appeared after an approval takes the approval back.
                if (hasActiveException && previous.Status is AttendanceRecordStatus.Approved or AttendanceRecordStatus.Calculated)
                {
                    previous.Status = AttendanceRecordStatus.PendingReview;
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
            }

            _db.AttendanceRecords.Add(candidate);
            current.Add(candidate);
            written++;
        }

        // Current records of shifts no longer scheduled that day (or of unscheduled work that no
        // longer exists) are retired without a successor.
        var keys = calculated.Select(c => c.Shift?.TimeScheduleShiftId).ToHashSet();
        var stale = existing.Where(r => r.IsCurrent && !keys.Contains(r.TimeScheduleShiftId)).ToList();
        stale.ForEach(r => r.IsCurrent = false);

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
        CancellationToken cancellationToken)
    {
        var stored = await _db.AttendanceExceptions
            .Where(e => e.EmployeeId == employeeId && e.AttendanceDate == day && e.Status != AttendanceExceptionStatus.Closed)
            .ToListAsync(cancellationToken);

        var active = new List<AttendanceException>();
        var matched = new HashSet<AttendanceException>();
        var raised = 0;
        foreach (var (shiftId, found) in detected)
        {
            var match = stored.FirstOrDefault(e => !matched.Contains(e)
                && e.TimeScheduleShiftId == shiftId
                && e.ExceptionType == found.Type
                && e.MissingEventType == found.MissingEventType
                && e.RelatedNormalizedTransactionId == found.NormalizedTransactionId);
            if (match is not null)
            {
                matched.Add(match);
                active.Add(match);
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
                Status = AttendanceExceptionStatus.Open,
                Description = found.Description,
                DetectedAt = now,
            };
            _db.AttendanceExceptions.Add(opened);
            active.Add(opened);
            raised++;
        }

        var resolved = 0;
        foreach (var gone in stored.Where(e => !matched.Contains(e)))
        {
            if (gone.Status == AttendanceExceptionStatus.Open)
            {
                gone.Status = AttendanceExceptionStatus.Closed;
                gone.ResolutionMethod = AttendanceExceptionResolutionMethod.AutoResolved;
                gone.ResolvedAt = now;
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
