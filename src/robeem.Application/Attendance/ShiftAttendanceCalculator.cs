using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The attendance calculation of one shift (study §8): pairs the punches, then derives working
/// time, break, lateness, early departure and overtime. Pure: no I/O, no clock.
/// </summary>
internal static class ShiftAttendanceCalculator
{
    private enum State
    {
        Off,
        Working,
        OnBreak,
    }

    /// <param name="shift">The scheduled shift; null for work outside any shift (no lateness, early departure or overtime).</param>
    /// <param name="punches">The punches the shift claims, in any order.</param>
    /// <param name="policy">The applicable policy: rounding, grace periods, overtime threshold, interpretation mode.</param>
    public static ShiftCalculation Calculate(ResolvedShift? shift, IReadOnlyList<AttendancePunch> punches, AttendancePolicy policy)
    {
        // Other events (e.g. door access) are not attendance.
        var ordered = InterpretUnlabeled(
            punches.Where(p => p.Type != NormalizedEventType.Other).OrderBy(p => p.Utc).ThenBy(p => p.NormalizedTransactionId).ToList(),
            shift,
            policy);

        // A punch still unlabeled can't be used, and is never guessed (CLAUDE.md §1.5, §1.6).
        var complete = ordered.All(p => p.Type != NormalizedEventType.Unknown);
        var typed = WithoutRepeats(ordered.Where(p => p.Type != NormalizedEventType.Unknown).ToList(), policy.DuplicateTimeWindowSeconds);
        var rounding = policy.RoundingRuleMinutes;

        // Pair the punches in time order. Elapsed time is measured on the UTC instants, so a shift
        // across midnight (or a daylight-saving change) needs no special case.
        var working = TimeSpan.Zero;
        var onBreak = TimeSpan.Zero;
        var state = State.Off;
        var segmentStart = default(DateTimeOffset);
        foreach (var punch in typed)
        {
            var at = Round(punch.Utc, rounding);
            switch (punch.Type, state)
            {
                case (NormalizedEventType.CheckIn, State.Off):
                    (state, segmentStart) = (State.Working, at);
                    break;
                case (NormalizedEventType.BreakOut, State.Working):
                    working += at - segmentStart;
                    (state, segmentStart) = (State.OnBreak, at);
                    break;
                case (NormalizedEventType.BreakIn, State.OnBreak):
                    onBreak += at - segmentStart;
                    (state, segmentStart) = (State.Working, at);
                    break;
                case (NormalizedEventType.CheckOut, State.Working):
                    working += at - segmentStart;
                    state = State.Off;
                    break;
                default:
                    // Out of sequence (e.g. a second check-in, a check-out during a break): the
                    // punch is skipped and the shift left for review. AttendanceExceptionDetector raises the exception.
                    complete = false;
                    break;
            }
        }

        var checkIn = typed.FirstOrDefault(p => p.Type == NormalizedEventType.CheckIn);
        var checkOut = typed.LastOrDefault(p => p.Type == NormalizedEventType.CheckOut);
        var breakOut = typed.FirstOrDefault(p => p.Type == NormalizedEventType.BreakOut);
        var breakIn = typed.FirstOrDefault(p => p.Type == NormalizedEventType.BreakIn && (breakOut is null || p.Utc >= breakOut.Utc));

        complete &= state == State.Off && checkIn is not null && checkOut is not null;
        if (shift?.BreakTime == true)
        {
            complete &= breakOut is not null && breakIn is not null;
        }

        var late = shift is not null && checkIn is not null
            ? Math.Max(0, WholeMinutes(RoundedLocal(checkIn, rounding) - shift.StartsAt) - policy.LateGraceMinutes)
            : 0;
        var early = shift is not null && checkOut is not null
            ? Math.Max(0, WholeMinutes(shift.EndsAt - RoundedLocal(checkOut, rounding)) - policy.EarlyDepartureGraceMinutes)
            : 0;

        // Overtime only from a complete shift: with a punch missing, the time worked is not known.
        // Below the policy's threshold, extra time is not overtime.
        var overtime = 0;
        if (shift is not null && complete)
        {
            var extra = WholeMinutes(working) - ScheduledMinutes(shift);
            overtime = extra > 0 && extra >= policy.OvertimeThresholdMinutes ? extra : 0;
        }

        return new ShiftCalculation
        {
            CheckIn = checkIn,
            CheckOut = checkOut,
            BreakOut = breakOut,
            BreakIn = breakIn,
            WorkingHours = working,
            BreakDuration = onBreak,
            LateMinutes = late,
            EarlyDepartureMinutes = early,
            OvertimeMinutes = overtime,
            IsComplete = complete,
            SourceIds = ordered.Where(p => p.NormalizedTransactionId.HasValue).Select(p => p.NormalizedTransactionId!.Value).Order().ToList(),
            CorrectionIds = ordered.Where(p => p.CorrectionId.HasValue).Select(p => p.CorrectionId!.Value).Order().ToList(),
            Punches = typed,
        };
    }

    /// <summary>
    /// Drops a punch of the same type as the one before it, when it falls inside the policy's
    /// duplicate window: the same event twice, not a sequence error (study §14.1). Normalization
    /// buckets punches, so two near-identical ones can still land in different buckets and both
    /// be Unique. The first punch of the pair is kept.
    /// </summary>
    private static List<AttendancePunch> WithoutRepeats(List<AttendancePunch> typed, int windowSeconds)
    {
        var window = TimeSpan.FromSeconds(windowSeconds);
        var kept = new List<AttendancePunch>(typed.Count);
        foreach (var punch in typed)
        {
            if (kept.Count > 0 && kept[^1].Type == punch.Type && punch.Utc - kept[^1].Utc <= window)
            {
                continue;
            }

            kept.Add(punch);
        }

        return kept;
    }

    /// <summary>The shift's scheduled working time, break excluded, through <see cref="Set.TimeSpan(string, string, string, string)"/>, which handles overnight shifts.</summary>
    private static int ScheduledMinutes(ResolvedShift shift)
    {
        var hours = shift.BreakTime
            ? Set.TimeSpan(shift.StartTime, shift.EndTime, shift.BreakStartTime, shift.BreakEndTime)
            : Set.TimeSpan(shift.StartTime, shift.EndTime);
        return (int)Math.Round(hours * 60);
    }

    /// <summary>
    /// Policy interpretation of unlabeled punches (CLAUDE.md §1.6). <c>Sequential</c> types a shift
    /// whose punches are all unlabeled, when their count matches the shift: two are a check-in
    /// and a check-out; four, on a shift with a break, are check-in, break-out, break-in and
    /// check-out. Anything else stays unlabeled, for review. Inferred types are marked as such.
    /// </summary>
    private static List<AttendancePunch> InterpretUnlabeled(List<AttendancePunch> ordered, ResolvedShift? shift, AttendancePolicy policy)
    {
        if (policy.UnlabeledPunchInterpretationMode != UnlabeledPunchInterpretationMode.Sequential
            || ordered.Count == 0
            || ordered.Any(p => p.Type != NormalizedEventType.Unknown))
        {
            return ordered;
        }

        NormalizedEventType[]? pattern = ordered.Count switch
        {
            2 when shift is not null => [NormalizedEventType.CheckIn, NormalizedEventType.CheckOut],
            4 when shift?.BreakTime == true => [NormalizedEventType.CheckIn, NormalizedEventType.BreakOut, NormalizedEventType.BreakIn, NormalizedEventType.CheckOut],
            _ => null,
        };

        return pattern is null
            ? ordered
            : ordered.Select((p, i) => p with { Type = pattern[i], Source = AttendancePunchSource.Inferred }).ToList();
    }

    /// <summary>Rounds to the nearest multiple of the policy's rounding rule; unchanged when it is 0.</summary>
    private static DateTimeOffset Round(DateTimeOffset utc, int roundingMinutes)
    {
        if (roundingMinutes <= 0)
        {
            return utc;
        }

        var step = TimeSpan.FromMinutes(roundingMinutes).Ticks;
        var rounded = (utc.UtcTicks + (step / 2)) / step * step;
        return new DateTimeOffset(rounded, TimeSpan.Zero);
    }

    private static DateTime RoundedLocal(AttendancePunch punch, int roundingMinutes) =>
        punch.Local + (Round(punch.Utc, roundingMinutes) - punch.Utc);

    private static int WholeMinutes(TimeSpan span) => (int)Math.Floor(span.TotalMinutes);
}
