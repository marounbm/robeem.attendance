using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads the obvious mistakes of a day's punches the way the rules say, so they do not become exceptions: a check-out where the check-in
/// belongs, a check-in where the check-out belongs, and one punch said twice. It only retypes a punch that exists (marked
/// <see cref="AttendancePunchSource.Inferred"/>), including a break-in or break-out of the wrong type near the scheduled break, or leaves one out of the calculation; it never adds a punch and never touches a stored one
/// (CLAUDE.md §1.5, §1.6). A punch an applied correction supplied is a person's decision and is never changed. Pure: no I/O.
/// </summary>
internal static class PunchFixer
{
    private enum State
    {
        Off,
        Working,
        OnBreak,
    }

    /// <summary>
    /// Applies the rules, in this order: one fix of the first two punches (keep the first, else keep the last of two check-ins), one fix of the last two (two
    /// check-outs keep the first; else a check-in then a check-out; else two check-outs keep the last), repeated punches of one kind anywhere in the day (also with no
    /// shift), a lone check-out at the start, a lone check-in at the end. A shift is needed to know what is near: with none (work outside a shift, hourly staff) nothing is fixed.
    /// </summary>
    /// <param name="typed">The punches in time order, without repeats.</param>
    /// <param name="shift">The scheduled shift.</param>
    /// <param name="rules">The rules that say which fixes are on and how near and how close a punch has to be.</param>
    /// <param name="retyped">How many punches were given another type.</param>
    /// <param name="ignored">How many punches were left out.</param>
    public static List<AttendancePunch> Fix(List<AttendancePunch> typed, ResolvedShift? shift, RuleSetSnapshot rules, out int retyped, out int ignored)
    {
        (retyped, ignored) = (0, 0);
        if (typed.Count == 0 || !rules.CanFixPunches)
        {
            return typed;
        }

        var result = new List<AttendancePunch>(typed);

        // Without a shift (work outside one, staff paid by the hour) nothing is near anything, but a punch said twice is still a repeat.
        if (shift is null)
        {
            ignored = FixRepeats(result, rules);
            return result;
        }

        var (hasBreak, overnight) = (shift.BreakTime, shift.EndsAt.Date > shift.StartsAt.Date);

        // Two punches at the start: a check-in, then another check-in or a check-out within minutes. The first is the check-in.
        if (result.Count >= 2
            && result[0].Type == NormalizedEventType.CheckIn
            && result[1].Type is NormalizedEventType.CheckIn or NormalizedEventType.CheckOut
            && FromDevice(result[0], result[1])
            && rules.Applies(RuleAction.KeepFirstMorningPunch, Facts(hasBreak, overnight, result[0], shift.StartsAt, result[1])))
        {
            result.RemoveAt(1);
            ignored++;
        }

        // Two check-ins at the start, close together: the person punched in, then again. The second counts and the first is left out. The punch that is
        // kept is the one that has to be near the start, as the first is for the rule above, which reads a pair first when both are on.
        else if (result.Count >= 2
            && result[0].Type == NormalizedEventType.CheckIn
            && result[1].Type == NormalizedEventType.CheckIn
            && FromDevice(result[0], result[1])
            && rules.Applies(RuleAction.KeepLastMorningPunch, KeptFacts(hasBreak, overnight, result[1], shift.StartsAt, result[0])))
        {
            result.RemoveAt(0);
            ignored++;
        }

        // A check-out, then a check-in within minutes, at the start: the check-out comes before any check-in, so it is a slip. It is left out and the check-in,
        // a punch the machine labelled as such, counts as it is.
        else if (result.Count >= 2
            && result[0].Type == NormalizedEventType.CheckOut
            && result[1].Type == NormalizedEventType.CheckIn
            && FromDevice(result[0], result[1])
            && rules.Applies(RuleAction.KeepMorningCheckInPunch, KeptFacts(hasBreak, overnight, result[1], shift.StartsAt, result[0])))
        {
            result.RemoveAt(0);
            ignored++;
        }

        // At the end, one fix reads the last two punches, the first rule that applies in this order:
        // two check-outs within minutes: the first is the check-out and the second is left out.
        if (result.Count >= 2
            && result[^1].Type == NormalizedEventType.CheckOut
            && result[^2].Type == NormalizedEventType.CheckOut
            && FromDevice(result[^2], result[^1])
            && rules.Applies(RuleAction.KeepFirstEveningPunch, Facts(hasBreak, overnight, result[^2], shift.EndsAt, result[^1])))
        {
            result.RemoveAt(result.Count - 1);
            ignored++;
        }

        // A check-in that follows a check-in with no check-out, then a check-out within minutes: the check-in is the check-out and the second is left out.
        else if (result.Count >= 2
            && result[^1].Type == NormalizedEventType.CheckOut
            && result[^2].Type == NormalizedEventType.CheckIn
            && FromDevice(result[^2], result[^1])
            && EndsWorking(result.Take(result.Count - 2))
            && rules.Applies(RuleAction.KeepFirstEveningCheckInPunch, Facts(hasBreak, overnight, result[^2], shift.EndsAt, result[^1])))
        {
            result[^2] = result[^2] with { Type = NormalizedEventType.CheckOut, Source = AttendancePunchSource.Inferred };
            retyped++;
            result.RemoveAt(result.Count - 1);
            ignored++;
        }

        // A check-in that follows a check-in, then a check-out within minutes, the other way: the check-in is left out and the check-out, a punch the machine
        // labelled as such, counts as it is. Nothing is retyped. The check-out is the punch that has to be near the end.
        else if (result.Count >= 2
            && result[^1].Type == NormalizedEventType.CheckOut
            && result[^2].Type == NormalizedEventType.CheckIn
            && FromDevice(result[^2], result[^1])
            && EndsWorking(result.Take(result.Count - 2))
            && rules.Applies(RuleAction.KeepEveningCheckOutPunch, KeptFacts(hasBreak, overnight, result[^1], shift.EndsAt, result[^2])))
        {
            result.RemoveAt(result.Count - 2);
            ignored++;
        }

        // Two check-outs within minutes, the other way: the second is the check-out and the first is left out. The punch that is kept, the second, is the one
        // that has to be near the end.
        else if (result.Count >= 2
            && result[^1].Type == NormalizedEventType.CheckOut
            && result[^2].Type == NormalizedEventType.CheckOut
            && FromDevice(result[^2], result[^1])
            && rules.Applies(RuleAction.KeepLastEveningPunch, KeptFacts(hasBreak, overnight, result[^1], shift.EndsAt, result[^2])))
        {
            result.RemoveAt(result.Count - 2);
            ignored++;
        }

        // Then two punches of the same kind one after the other, anywhere in the day, until none is left.
        ignored += FixRepeats(result, rules);

        // No check-in at all, and the first punch is a check-out where the shift starts: it is the check-in.
        if (result.Count > 0
            && result[0].Type == NormalizedEventType.CheckOut
            && result.All(p => p.Type != NormalizedEventType.CheckIn)
            && FromDevice(result[0])
            && rules.Applies(RuleAction.LoneCheckOutIsCheckIn, Facts(hasBreak, overnight, result[0], shift.StartsAt)))
        {
            result[0] = result[0] with { Type = NormalizedEventType.CheckIn, Source = AttendancePunchSource.Inferred };
            retyped++;
        }

        // No check-out at all, and the last punch is a check-in where the shift ends: it is the check-out.
        if (result.Count > 0
            && result[^1].Type == NormalizedEventType.CheckIn
            && result.All(p => p.Type != NormalizedEventType.CheckOut)
            && FromDevice(result[^1])
            && rules.Applies(RuleAction.LoneCheckInIsCheckOut, Facts(hasBreak, overnight, result[^1], shift.EndsAt)))
        {
            result[^1] = result[^1] with { Type = NormalizedEventType.CheckOut, Source = AttendancePunchSource.Inferred };
            retyped++;
        }

        retyped += FixBreakPunches(result, shift, hasBreak, overnight, rules);
        return result;
    }

    /// <summary>
    /// Leaves out one of two punches of the same kind that follow each other within the number of minutes the rules allow, the first or the later one as the rules
    /// say, until none is left (so three taps become one). A break-out or break-in repeated keeps the first. A punch a person's correction supplied is never read.
    /// </summary>
    private static int FixRepeats(List<AttendancePunch> result, RuleSetSnapshot rules)
    {
        var ignored = 0;
        var i = 0;
        while (i < result.Count - 1)
        {
            var (first, second) = (result[i], result[i + 1]);
            if (first.Type == second.Type && FromDevice(first, second) && RepeatKeeps(first.Type, rules, (int)Math.Ceiling((second.Utc - first.Utc).TotalMinutes)) is { } keepsFirst)
            {
                // Staying at the same place reads the punch that is left against the next one.
                result.RemoveAt(keepsFirst ? i + 1 : i);
                ignored++;
                continue;
            }

            i++;
        }

        return ignored;
    }

    /// <summary>Whether two close punches of this kind keep the first (true) or the last (false); null when no rule reads them. Keeping the first wins when both are on.</summary>
    private static bool? RepeatKeeps(NormalizedEventType type, RuleSetSnapshot rules, int gapMinutes)
    {
        var facts = new RuleFacts { PunchGapMinutes = gapMinutes };
        return type switch
        {
            NormalizedEventType.CheckIn => rules.Applies(RuleAction.KeepFirstRepeatedCheckIn, facts) ? true : rules.Applies(RuleAction.KeepLastRepeatedCheckIn, facts) ? false : null,
            NormalizedEventType.CheckOut => rules.Applies(RuleAction.KeepFirstRepeatedCheckOut, facts) ? true : rules.Applies(RuleAction.KeepLastRepeatedCheckOut, facts) ? false : null,
            NormalizedEventType.BreakOut or NormalizedEventType.BreakIn => rules.Applies(RuleAction.KeepFirstRepeatedBreakPunch, facts) ? true : null,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a break punch of the wrong type the way the rules say, walking the day in order: a break-in where no break is open and the punch is near the
    /// scheduled start of the break is the break-out (a break-in said twice, or in place of the break-out); a break-out where a break is already open and the
    /// punch is near the scheduled end of the break is the break-in (a break-out said twice, or in place of the break-in). Only the type changes.
    /// </summary>
    private static int FixBreakPunches(List<AttendancePunch> result, ResolvedShift shift, bool hasBreak, bool overnight, RuleSetSnapshot rules)
    {
        if (shift is not { BreakTime: true, BreakStartsAt: { } breakStart, BreakEndsAt: { } breakEnd })
        {
            return 0;
        }

        var retyped = 0;
        var state = State.Off;
        for (var i = 0; i < result.Count; i++)
        {
            var punch = result[i];
            if (FromDevice(punch))
            {
                if (punch.Type == NormalizedEventType.BreakIn
                    && state == State.Working
                    && rules.Applies(RuleAction.BreakInIsBreakOut, Facts(hasBreak, overnight, punch, breakStart)))
                {
                    punch = result[i] = punch with { Type = NormalizedEventType.BreakOut, Source = AttendancePunchSource.Inferred };
                    retyped++;
                }
                else if (punch.Type == NormalizedEventType.BreakOut
                    && state == State.OnBreak
                    && rules.Applies(RuleAction.BreakOutIsBreakIn, Facts(hasBreak, overnight, punch, breakEnd)))
                {
                    punch = result[i] = punch with { Type = NormalizedEventType.BreakIn, Source = AttendancePunchSource.Inferred };
                    retyped++;
                }
            }

            state = (punch.Type, state) switch
            {
                (NormalizedEventType.CheckIn, State.Off) => State.Working,
                (NormalizedEventType.BreakOut, State.Working) => State.OnBreak,
                (NormalizedEventType.BreakIn, State.OnBreak) => State.Working,
                (NormalizedEventType.CheckOut, State.Working) => State.Off,
                _ => state,
            };
        }

        return retyped;
    }

    /// <summary>A punch a person's correction supplied is never read differently by a rule.</summary>
    private static bool FromDevice(params AttendancePunch[] punches) => punches.All(p => p.Source != AttendancePunchSource.Corrected);

    private static RuleFacts Facts(bool hasBreak, bool overnight, AttendancePunch punch, DateTime boundary, AttendancePunch? second = null) => new()
    {
        ShiftHasBreak = hasBreak,
        ShiftIsOvernight = overnight,
        PunchOffsetMinutes = (int)Math.Ceiling(Math.Abs((punch.Local - boundary).TotalMinutes)),
        PunchGapMinutes = second is null ? 0 : (int)Math.Ceiling((second.Utc - punch.Utc).TotalMinutes),
    };

    /// <summary>The facts of a pair read by keeping its <em>later</em> punch: how near the kept punch is to the boundary, and how long after the earlier one it came.</summary>
    private static RuleFacts KeptFacts(bool hasBreak, bool overnight, AttendancePunch kept, DateTime boundary, AttendancePunch earlier) =>
        Facts(hasBreak, overnight, kept, boundary) with { PunchGapMinutes = (int)Math.Ceiling((kept.Utc - earlier.Utc).TotalMinutes) };

    /// <summary>Whether the punches, taken in order, leave the person at work (checked in, not on a break).</summary>
    private static bool EndsWorking(IEnumerable<AttendancePunch> punches)
    {
        var state = State.Off;
        foreach (var punch in punches)
        {
            state = (punch.Type, state) switch
            {
                (NormalizedEventType.CheckIn, State.Off) => State.Working,
                (NormalizedEventType.BreakOut, State.Working) => State.OnBreak,
                (NormalizedEventType.BreakIn, State.OnBreak) => State.Working,
                (NormalizedEventType.CheckOut, State.Working) => State.Off,
                _ => state,
            };
        }

        return state == State.Working;
    }
}
