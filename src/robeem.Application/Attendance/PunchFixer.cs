using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads the obvious mistakes of a day's punches the way the rules say, so they do not become exceptions: a check-out where the check-in
/// belongs, a check-in where the check-out belongs, and one punch said twice. It only retypes a punch that exists (marked
/// <see cref="AttendancePunchSource.Inferred"/>) or leaves one out of the calculation; it never adds a punch and never touches a stored one
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
    /// Applies the rules, in this order: the first of two close punches at the start, the first of two close punches at the end, a lone check-out
    /// at the start, a lone check-in at the end. A shift is needed to know what is near: with none (work outside a shift, hourly staff) nothing is fixed.
    /// </summary>
    /// <param name="typed">The punches in time order, without repeats.</param>
    /// <param name="shift">The scheduled shift.</param>
    /// <param name="rules">The rules that say which fixes are on and how near and how close a punch has to be.</param>
    /// <param name="retyped">How many punches were given another type.</param>
    /// <param name="ignored">How many punches were left out.</param>
    public static List<AttendancePunch> Fix(List<AttendancePunch> typed, ResolvedShift? shift, RuleSetSnapshot rules, out int retyped, out int ignored)
    {
        (retyped, ignored) = (0, 0);
        if (shift is null || typed.Count == 0 || !rules.CanFixPunches)
        {
            return typed;
        }

        var result = new List<AttendancePunch>(typed);
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

        // Two punches at the end: a check-out, or a check-in that follows a check-in with no check-out, then a check-out within minutes. The first is the check-out.
        if (result.Count >= 2
            && result[^1].Type == NormalizedEventType.CheckOut
            && result[^2].Type is NormalizedEventType.CheckOut or NormalizedEventType.CheckIn
            && FromDevice(result[^2], result[^1])
            && (result[^2].Type == NormalizedEventType.CheckOut || EndsWorking(result.Take(result.Count - 2)))
            && rules.Applies(RuleAction.KeepFirstEveningPunch, Facts(hasBreak, overnight, result[^2], shift.EndsAt, result[^1])))
        {
            if (result[^2].Type == NormalizedEventType.CheckIn)
            {
                result[^2] = result[^2] with { Type = NormalizedEventType.CheckOut, Source = AttendancePunchSource.Inferred };
                retyped++;
            }

            result.RemoveAt(result.Count - 1);
            ignored++;
        }

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

        return result;
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
