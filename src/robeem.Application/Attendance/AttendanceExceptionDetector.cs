using System.Globalization;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Finds the exceptions in one shift's punches (study §9, §14.1). Pure: no I/O, and the clock is a
/// parameter. It never invents a punch (CLAUDE.md §1.5); it only reports that one is absent.
/// </summary>
/// <remarks>
/// A missing punch is raised at once when later punches prove it was due, for instance a
/// check-out with no check-in. When nothing proves it, it might simply not have happened yet, so
/// it waits until the shift has ended and the policy's
/// <see cref="AttendancePolicy.MissingPunchGraceMinutes"/> have passed.
/// </remarks>
internal static class AttendanceExceptionDetector
{
    private enum State
    {
        Off,
        Working,
        OnBreak,
    }

    /// <param name="shift">The scheduled shift; null for work outside any shift.</param>
    /// <param name="day">The attendance date, used to end the day of unscheduled work.</param>
    /// <param name="calculation">The calculation of the shift: its punches are the interpreted, de-duplicated ones.</param>
    /// <param name="policy">The applicable policy: the grace window.</param>
    /// <param name="now">The current local wall-clock time.</param>
    public static IReadOnlyList<DetectedException> Detect(ResolvedShift? shift, DateTime day, ShiftCalculation calculation, AttendancePolicy policy, DateTime now)
    {
        var punches = calculation.Punches;
        var found = new List<DetectedException>();

        var expectedEnd = shift?.EndsAt ?? day.AddDays(1);
        var graceOver = now >= expectedEnd.AddMinutes(policy.MissingPunchGraceMinutes);

        // A scheduled shift without a single punch is a no-show. Nothing is expected on a day off,
        // or when no schedule is configured, and then there is no shift to get here.
        if (calculation.SourceIds.Count == 0 && punches.Count == 0)
        {
            if (shift is not null && graceOver)
            {
                found.Add(Missing(NormalizedEventType.CheckIn, "No punch was recorded for a scheduled shift."));
                found.Add(Missing(NormalizedEventType.CheckOut, "No punch was recorded for a scheduled shift."));
            }

            return found;
        }

        var breakExpected = shift?.BreakTime == true;
        var hasCheckIn = punches.Any(p => p.Type == NormalizedEventType.CheckIn);

        // With no check-in at all, the employee is taken to have been working: the check-in is
        // reported missing below, and the rest of the punches are checked as if it were there.
        var state = hasCheckIn ? State.Off : State.Working;
        var seenCheckIn = false;
        AttendancePunch? openBreak = null;

        foreach (var punch in punches)
        {
            switch (punch.Type, state)
            {
                case (NormalizedEventType.CheckIn, State.Off):
                    (state, seenCheckIn) = (State.Working, true);
                    break;
                case (NormalizedEventType.CheckIn, State.Working):
                    found.Add(Invalid(punch, "A check-in follows a check-in, with no check-out between them."));
                    break;
                case (NormalizedEventType.CheckIn, State.OnBreak):
                    found.Add(Invalid(punch, "A check-in during a break."));
                    break;

                case (NormalizedEventType.BreakOut, State.Working):
                    (state, openBreak) = (State.OnBreak, punch);
                    break;
                case (NormalizedEventType.BreakOut, State.OnBreak):
                    found.Add(Invalid(punch, "A break-out follows a break-out, with no break-in between them."));
                    break;
                case (NormalizedEventType.BreakOut, State.Off):
                    found.Add(Invalid(punch, seenCheckIn ? "A break-out after the check-out." : "A break-out before the first check-in."));
                    break;

                case (NormalizedEventType.BreakIn, State.OnBreak):
                    (state, openBreak) = (State.Working, null);
                    break;
                case (NormalizedEventType.BreakIn, State.Working):
                    // The break-in proves a break-out was due. A shift with no break can't be
                    // missing one, so there the order is wrong instead.
                    found.Add(breakExpected
                        ? Missing(NormalizedEventType.BreakOut, "A break-in has no break-out before it.")
                        : Invalid(punch, "A break-in with no break-out before it, on a shift without a break."));
                    break;
                case (NormalizedEventType.BreakIn, State.Off):
                    found.Add(Invalid(punch, seenCheckIn ? "A break-in after the check-out." : "A break-in before the first check-in."));
                    break;

                case (NormalizedEventType.CheckOut, State.Working):
                    state = State.Off;
                    break;
                case (NormalizedEventType.CheckOut, State.OnBreak):
                    // The check-out proves the break-in was due.
                    found.Add(UnmatchedBreakOut(openBreak!, breakExpected, "The break-out has no break-in before the check-out."));
                    (state, openBreak) = (State.Off, null);
                    break;
                case (NormalizedEventType.CheckOut, State.Off):
                    found.Add(Invalid(punch, seenCheckIn ? "A check-out follows a check-out, with no check-in between them." : "A check-out before the first check-in."));
                    break;
            }
        }

        if (!hasCheckIn)
        {
            found.Add(Missing(NormalizedEventType.CheckIn, "Punches exist, but there is no check-in."));
        }

        // The shift is not over for the employee yet while its end, plus the grace, is still ahead.
        if (state != State.Off && graceOver)
        {
            if (state == State.OnBreak)
            {
                found.Add(UnmatchedBreakOut(openBreak!, breakExpected, "The break-out has no break-in, and the shift is over."));
            }

            found.Add(Missing(NormalizedEventType.CheckOut, "The shift is over and there is no check-out."));
        }

        // A shift with a break, worked without a single break punch: either one can't have been
        // missed for sure while the shift is still running. The policy decides whether that is a problem at all.
        var hasCheckOut = punches.Any(p => p.Type == NormalizedEventType.CheckOut);
        var hasBreakPunch = punches.Any(p => p.Type is NormalizedEventType.BreakOut or NormalizedEventType.BreakIn);
        if (breakExpected && !hasBreakPunch && (hasCheckOut || graceOver) && policy.RaiseExceptionOnNoBreak)
        {
            found.Add(Missing(NormalizedEventType.BreakOut, "The shift has a break, but no break-out was recorded."));
            found.Add(Missing(NormalizedEventType.BreakIn, "The shift has a break, but no break-in was recorded."));
        }

        AddBreakFindings(found, calculation, shift, policy);

        // One report per problem: a repeated cause (two stray break-ins) is still one missing break-out.
        return found
            .GroupBy(d => (d.Type, d.MissingEventType, d.NormalizedTransactionId))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Raises the long, late and early breaks that the policy asks to be raised. The finding is on the record either way; here it only
    /// becomes an exception when the policy says so. It is tied to the break-out punch, so accepting one break does not accept another.
    /// </summary>
    private static void AddBreakFindings(List<DetectedException> found, ShiftCalculation calculation, ResolvedShift? shift, AttendancePolicy policy)
    {
        if (shift?.BreakStartsAt is not { } scheduledStart || calculation.BreakOut is not { } breakOut)
        {
            return;
        }

        var scheduled = scheduledStart.ToString("HH:mm", CultureInfo.InvariantCulture);
        var findings = calculation.BreakFindings;
        if (findings.HasFlag(BreakFindings.LongBreak) && policy.RaiseExceptionOnLongBreak)
        {
            found.Add(new DetectedException(
                AttendanceExceptionType.LongBreak,
                null,
                breakOut.NormalizedTransactionId,
                $"The break lasted {Minutes((int)calculation.BreakDuration.TotalMinutes)}, {Minutes(calculation.BreakExcessMinutes)} longer than the scheduled {Minutes(shift.BreakMinutes)}."));
        }

        if (findings.HasFlag(BreakFindings.LateBreak) && policy.RaiseExceptionOnLateBreak)
        {
            found.Add(new DetectedException(
                AttendanceExceptionType.LateBreak,
                null,
                breakOut.NormalizedTransactionId,
                $"The break started {Minutes(calculation.BreakStartOffsetMinutes)} after the scheduled {scheduled}."));
        }

        if (findings.HasFlag(BreakFindings.EarlyBreak) && policy.RaiseExceptionOnEarlyBreak)
        {
            found.Add(new DetectedException(
                AttendanceExceptionType.EarlyBreak,
                null,
                breakOut.NormalizedTransactionId,
                $"The break started {Minutes(-calculation.BreakStartOffsetMinutes)} before the scheduled {scheduled}."));
        }
    }

    private static string Minutes(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";

    private static DetectedException Missing(NormalizedEventType missing, string description) =>
        new(AttendanceExceptionType.MissingPunch, missing, null, description);

    private static DetectedException Invalid(AttendancePunch punch, string description) =>
        new(AttendanceExceptionType.InvalidSequence, null, punch.NormalizedTransactionId, description);

    /// <summary>A break-out nothing closed: a missing break-in on a shift with a break, otherwise an invalid sequence.</summary>
    private static DetectedException UnmatchedBreakOut(AttendancePunch breakOut, bool breakExpected, string description) =>
        breakExpected ? Missing(NormalizedEventType.BreakIn, description) : Invalid(breakOut, description);
}
