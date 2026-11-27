using System.Globalization;
using robeem.Application.HR.Absence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The arithmetic of the compensation report, with no I/O: which lines a payable day gives, how decisions apply to them, the penalties
/// and the totals. Kept apart from <see cref="PayrollCompensationService"/>, which reads and saves.
/// </summary>
internal static class CompensationCalculator
{
    private static readonly CompensationDecisionKind[] OvertimeDecisions = [CompensationDecisionKind.Ignore, CompensationDecisionKind.Overtime];

    private static readonly CompensationDecisionKind[] UndertimeDecisions = [CompensationDecisionKind.Ignore, CompensationDecisionKind.Penalise];

    /// <summary>The decisions an under time line takes, apart from leave.</summary>
    public static IReadOnlyList<CompensationDecisionKind> UndertimeChoices => UndertimeDecisions;

    /// <summary>
    /// The time missed at the start and at the end of the day's shifts: from the shift start to the check-in, and from the check-out to the
    /// shift end. In whole minutes, so that a leave over it covers all of it.
    /// </summary>
    public static List<(DateTime Start, DateTime End)> MissedWindows(PayrollAttendanceDay day)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        foreach (var shift in day.Shifts)
        {
            if (shift is { LateMinutes: > 0, ScheduledStart: { } scheduledStart, ActualCheckIn: { } checkIn } && checkIn > scheduledStart)
            {
                AddWindow(windows, scheduledStart, checkIn);
            }

            if (shift is { EarlyDepartureMinutes: > 0, ScheduledEnd: { } scheduledEnd, ActualCheckOut: { } checkOut } && checkOut < scheduledEnd)
            {
                AddWindow(windows, checkOut, scheduledEnd);
            }
        }

        return windows;
    }

    /// <summary>
    /// The lines of one payable day: its overtime, its under time, and its late or early minutes when they have no line of their own.
    /// Staff paid by the hour have none: there is no schedule to be late for.
    /// </summary>
    public static IEnumerable<CompensationLine> LinesOf(
        bool hourly,
        AttendancePolicy policy,
        PayrollAttendanceDay day,
        bool isHoliday,
        IReadOnlyList<PayCompensationDecision> decisions,
        IReadOnlyDictionary<int, string> deciders)
    {
        if (hourly)
        {
            return [];
        }

        var lines = new List<CompensationLine>();
        var date = day.AttendanceDate.Date;
        var worked = Minutes(day.Shifts.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.WorkingHours));
        var shiftOvertime = day.Shifts.Where(s => s.TimeScheduleShiftId is not null).Sum(s => s.OvertimeMinutes);
        var shiftlessWork = Minutes(day.Shifts.Where(s => s.TimeScheduleShiftId is null).Aggregate(TimeSpan.Zero, (sum, s) => sum + s.WorkingHours));
        var late = day.Shifts.Sum(s => s.LateMinutes);
        var early = day.Shifts.Sum(s => s.EarlyDepartureMinutes);
        var under = Minutes(day.TotalUndertimeHours);
        var scheduled = string.Join("; ", day.Shifts
            .Where(s => s is { ScheduledStart: not null, ScheduledEnd: not null })
            .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.ScheduledStart:HH:mm}–{s.ScheduledEnd:HH:mm}")));

        // Overtime: all the work of a holiday, work on a day off, or the extra time of a shift, as the schedule's rules count it.
        var (overtime, kind) = (0, string.Empty);
        if (isHoliday)
        {
            if (policy.PayHolidayWorkIsOvertime && worked > 0)
            {
                (overtime, kind) = (worked, "Holiday");
            }
        }
        else
        {
            var dayOff = policy.PayDayOffWorkIsOvertime ? shiftlessWork : 0;
            if (shiftOvertime + dayOff > 0)
            {
                (overtime, kind) = (shiftOvertime + dayOff, dayOff > 0 ? "DayOff" : "Weekday");
            }
        }

        var lateEarlyPenalty = Weigh(late, policy.PayLatePenaltyPercent) + Weigh(early, policy.PayEarlyPenaltyPercent);
        var placeable = MissedWindows(day).Sum(w => (int)(w.End - w.Start).TotalMinutes);

        if (overtime > 0)
        {
            var label = isHoliday ? "Public holiday" : kind == "DayOff" && scheduled.Length == 0 ? "Day off" : scheduled;
            var decided = Latest(decisions, date, CompensationLineKind.Overtime);
            var valid = decided is not null && decided.Minutes == overtime;
            lines.Add(new CompensationLine
            {
                Date = date,
                LineKind = CompensationLine.Overtime,
                Scheduled = label,
                OvertimeKind = kind,
                WorkedMinutes = worked,
                Minutes = overtime,
                Decision = valid ? decided!.Decision.ToString() : null,
                DecidedMinutes = valid ? decided!.Minutes : 0,
                DecidedByName = valid ? deciders.GetValueOrDefault(decided!.DecidedById) : null,
                DecidedAt = valid ? decided!.DecidedAt : null,
                NeedsDecision = !valid,
                AllowedDecisions = OvertimeDecisions.Select(d => d.ToString()).ToList(),
            });
        }

        if (under > 0)
        {
            var decided = Latest(decisions, date, CompensationLineKind.Undertime);
            var valid = decided is not null && decided.Minutes == under;
            var penalised = valid && decided!.Decision == CompensationDecisionKind.Penalise;
            var allowed = UndertimeDecisions.Select(d => d.ToString()).ToList();
            if (placeable > 0)
            {
                allowed.Add(nameof(CompensationDecisionKind.PaidLeave));
                allowed.Add(nameof(CompensationDecisionKind.UnpaidLeave));
            }

            lines.Add(new CompensationLine
            {
                Date = date,
                LineKind = CompensationLine.Undertime,
                Scheduled = scheduled,
                WorkedMinutes = worked,
                Minutes = under,
                LateMinutes = late,
                EarlyMinutes = early,

                // Late and early minutes weigh by their own percents; only the under time beyond them is the penalised under time, so no minute counts twice.
                PenaltyMinutes = lateEarlyPenalty + (penalised ? Weigh(Math.Max(0, under - late - early), policy.PayUndertimePenaltyPercent) : 0),
                Decision = valid ? decided!.Decision.ToString() : null,
                DecidedMinutes = valid ? decided!.Minutes : 0,
                DecidedByName = valid ? deciders.GetValueOrDefault(decided!.DecidedById) : null,
                DecidedAt = valid ? decided!.DecidedAt : null,
                NeedsDecision = !valid,
                AllowedDecisions = allowed,
                LeavePlaceableMinutes = placeable,
            });
        }
        else if (lateEarlyPenalty > 0)
        {
            lines.Add(new CompensationLine
            {
                Date = date,
                LineKind = CompensationLine.Penalty,
                Scheduled = scheduled,
                WorkedMinutes = worked,
                LateMinutes = late,
                EarlyMinutes = early,
                PenaltyMinutes = lateEarlyPenalty,
            });
        }

        return lines;
    }

    /// <summary>
    /// A line for a leave decision whose leave still exists: the under time was covered, so there is nothing left to decide, but the report still says so.
    /// </summary>
    public static CompensationLine CoveredLine(PayCompensationDecision decision, IReadOnlyDictionary<int, string> deciders) => new()
    {
        Date = decision.AttendanceDate.Date,
        LineKind = CompensationLine.Undertime,
        Scheduled = "Covered by leave",
        Minutes = decision.Minutes,
        Decision = decision.Decision.ToString(),
        DecidedMinutes = decision.Minutes,
        DecidedByName = deciders.GetValueOrDefault(decision.DecidedById),
        DecidedAt = decision.DecidedAt,
        IsCoveredByLeave = true,
    };

    /// <summary>Puts the lines in date order (overtime, then under time, then penalty on a day) and numbers the penalties as they accumulate.</summary>
    public static List<CompensationLine> Arrange(IEnumerable<CompensationLine> lines, bool closed)
    {
        var running = 0;
        return lines
            .OrderBy(l => l.Date)
            .ThenBy(l => l.LineKind == CompensationLine.Overtime ? 0 : l.LineKind == CompensationLine.Undertime ? 1 : 2)
            .ThenBy(l => l.IsCoveredByLeave ? 0 : 1)
            .Select(l => new CompensationLine
            {
                Date = l.Date,
                LineKind = l.LineKind,
                Scheduled = l.Scheduled,
                OvertimeKind = l.OvertimeKind,
                WorkedMinutes = l.WorkedMinutes,
                Minutes = l.Minutes,
                LateMinutes = l.LateMinutes,
                EarlyMinutes = l.EarlyMinutes,
                PenaltyMinutes = l.PenaltyMinutes,
                RunningPenaltyMinutes = running += l.PenaltyMinutes,
                Decision = l.Decision,
                DecidedMinutes = l.DecidedMinutes,
                DecidedByName = l.DecidedByName,
                DecidedAt = l.DecidedAt,

                // A closed period waits for nothing: its decisions are locked.
                NeedsDecision = l.NeedsDecision && !closed,
                IsCoveredByLeave = l.IsCoveredByLeave,
                AllowedDecisions = closed ? [] : l.AllowedDecisions,
                LeavePlaceableMinutes = l.LeavePlaceableMinutes,
            })
            .ToList();
    }

    /// <summary>
    /// The totals. Overtime nobody decided counts; under time nobody decided stays, and offsets the overtime: the balance is the overtime
    /// counted minus the under time that remains, and the report ends with one overtime line of that balance.
    /// </summary>
    public static CompensationSummary Summarise(IReadOnlyList<PayrollAttendanceDay> days, IReadOnlyList<CompensationLine> lines)
    {
        var overtime = lines.Where(l => l.LineKind == CompensationLine.Overtime).ToList();
        var undertime = lines.Where(l => l.LineKind == CompensationLine.Undertime).ToList();
        var live = undertime.Where(l => !l.IsCoveredByLeave).ToList();

        var overtimeTotal = overtime.Sum(l => l.Minutes);
        var overtimeIgnored = overtime.Where(l => l is { NeedsDecision: false, Decision: nameof(CompensationDecisionKind.Ignore) }).Sum(l => l.Minutes);
        var counted = overtimeTotal - overtimeIgnored;
        var remaining = live.Where(l => l.Decision is null || l.NeedsDecision).Sum(l => l.Minutes);
        var balance = counted - remaining;
        var worked = days.Sum(d => Minutes(d.Shifts.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.WorkingHours)));

        return new CompensationSummary
        {
            PaidDays = days.Count,
            WorkedMinutes = worked,
            RegularMinutes = Math.Max(0, worked - overtimeTotal),
            OvertimeMinutes = overtimeTotal,
            UndertimeMinutes = undertime.Sum(l => l.Minutes),
            OvertimeIgnoredMinutes = overtimeIgnored,
            OvertimeAcceptedMinutes = overtime.Where(l => l is { NeedsDecision: false, Decision: nameof(CompensationDecisionKind.Overtime) }).Sum(l => l.Minutes),
            UndertimeIgnoredMinutes = live.Where(l => l is { NeedsDecision: false, Decision: nameof(CompensationDecisionKind.Ignore) }).Sum(l => l.Minutes),
            UndertimePaidLeaveMinutes = undertime.Where(l => l is { IsCoveredByLeave: true, Decision: nameof(CompensationDecisionKind.PaidLeave) }).Sum(l => l.Minutes),
            UndertimeUnpaidLeaveMinutes = undertime.Where(l => l is { IsCoveredByLeave: true, Decision: nameof(CompensationDecisionKind.UnpaidLeave) }).Sum(l => l.Minutes),
            UndertimePenalisedMinutes = live.Where(l => l is { NeedsDecision: false, Decision: nameof(CompensationDecisionKind.Penalise) }).Sum(l => l.Minutes),
            OvertimeCountedMinutes = counted,
            UndertimeRemainingMinutes = remaining,
            BalanceMinutes = balance,
            OvertimeLineMinutes = Math.Max(balance, 0),
            DeficitMinutes = Math.Max(-balance, 0),
            PenaltyMinutes = lines.Sum(l => l.PenaltyMinutes),
            UndecidedLines = lines.Count(l => l.NeedsDecision),
        };
    }

    public static IEnumerable<int> ParseIds(string ids) =>
        ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0);

    /// <summary>Whether this whole-day entry is a public holiday on the date.</summary>
    public static bool IsHolidayOn(IEnumerable<ExcusedPeriod> periods, DateTime date) =>
        periods.Any(p => p is { Kind: ExcusedKind.Holiday, IsWholeDay: true } && p.Overlaps(date, date.AddDays(1)));

    public static int Minutes(TimeSpan time) => (int)Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero);

    private static int Weigh(int minutes, int percent) => (int)Math.Round(minutes * percent / 100.0, MidpointRounding.AwayFromZero);

    private static void AddWindow(List<(DateTime Start, DateTime End)> windows, DateTime start, DateTime end)
    {
        // A leave is written in whole minutes: it starts at the minute the time was missed from and ends at the minute after it.
        var from = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Unspecified);
        var to = new DateTime(end.Year, end.Month, end.Day, end.Hour, end.Minute, 0, DateTimeKind.Unspecified);
        if (end.Second > 0 || end.Millisecond > 0)
        {
            to = to.AddMinutes(1);
        }

        if (to > from)
        {
            windows.Add((from, to));
        }
    }

    /// <summary>The latest decision on a line that is not a leave: a leave decision is a line of its own and is never replaced.</summary>
    private static PayCompensationDecision? Latest(IReadOnlyList<PayCompensationDecision> decisions, DateTime date, CompensationLineKind kind) =>
        decisions.LastOrDefault(d => d.AttendanceDate.Date == date
            && d.LineKind == kind
            && d.Decision is not (CompensationDecisionKind.PaidLeave or CompensationDecisionKind.UnpaidLeave));
}
