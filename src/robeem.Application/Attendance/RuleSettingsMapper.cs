using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads the standard rules of a scope as <see cref="RuleSettings"/> and writes settings back into them. The standard rules are the twenty-nine the
/// starting rules are made of, each once, with its usual conditions: only their switches and numbers differ. Rules that were added, removed or given
/// other conditions are not standard, and are changed in the Rules grid instead. Pure: no I/O.
/// </summary>
public static class RuleSettingsMapper
{
    public const int MaxMinutes = 1440;

    private const string NotStandard = "These rules have been customised (a rule was added or removed, or a rule has other conditions), so they can't be shown as simple settings. Use the advanced rules.";

    private static readonly Shape LongBreak = new(RuleAction.RaiseException, RuleFinding.LongBreak, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape LateBreak = new(RuleAction.RaiseException, RuleFinding.LateBreak, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape EarlyBreak = new(RuleAction.RaiseException, RuleFinding.EarlyBreak, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape NoBreak = new(RuleAction.RaiseException, RuleFinding.NoBreak, []);
    private static readonly Shape ShortHours = new(RuleAction.RaiseException, RuleFinding.ShortHours, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape LateCheckIn = new(RuleAction.RaiseException, RuleFinding.LateCheckIn, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape EarlyCheckOut = new(RuleAction.RaiseException, RuleFinding.EarlyCheckOut, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape Overtime = new(RuleAction.RaiseException, RuleFinding.Overtime, [(RuleFact.MinutesOver, RuleOperator.GreaterThan)]);
    private static readonly Shape UnscheduledWork = new(RuleAction.RaiseException, RuleFinding.UnscheduledWork, []);
    private static readonly Shape NoShow = new(RuleAction.RaiseException, RuleFinding.NoShow, [(RuleFact.MinutesAfterShiftEnd, RuleOperator.GreaterOrEqual)]);
    private static readonly Shape MissingCheckOut = new(RuleAction.RaiseException, RuleFinding.MissingCheckOut, [(RuleFact.MinutesAfterShiftEnd, RuleOperator.GreaterOrEqual)]);
    private static readonly Shape ShortBreak = new(RuleAction.IgnoreShortBreak, null, [(RuleFact.BreakMinutes, RuleOperator.LessThan)]);

    private static readonly Shape ReadGap = new(
        RuleAction.InferBreak,
        null,
        [
            (RuleFact.ShiftHasBreak, RuleOperator.Equals),
            (RuleFact.GapStartOffsetMinutes, RuleOperator.LessOrEqual),
            (RuleFact.GapMinutes, RuleOperator.LessOrEqual),
            (RuleFact.GapExcusedPercent, RuleOperator.LessOrEqual),
        ]);

    private static readonly Shape LoneCheckOut = new(RuleAction.LoneCheckOutIsCheckIn, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape LoneCheckIn = new(RuleAction.LoneCheckInIsCheckOut, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape MorningPair = new(RuleAction.KeepFirstMorningPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape EveningPair = new(RuleAction.KeepFirstEveningPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape EveningLast = new(RuleAction.KeepLastEveningPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape EveningInThenOut = new(RuleAction.KeepFirstEveningCheckInPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape MorningLast = new(RuleAction.KeepLastMorningPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape EveningInThenOutLast = new(RuleAction.KeepEveningCheckOutPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape MorningOutThenIn = new(RuleAction.KeepMorningCheckInPunch, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual), (RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape RepeatCheckInFirst = new(RuleAction.KeepFirstRepeatedCheckIn, null, [(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape RepeatCheckInLast = new(RuleAction.KeepLastRepeatedCheckIn, null, [(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape RepeatCheckOutFirst = new(RuleAction.KeepFirstRepeatedCheckOut, null, [(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape RepeatCheckOutLast = new(RuleAction.KeepLastRepeatedCheckOut, null, [(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape RepeatBreakPunchFirst = new(RuleAction.KeepFirstRepeatedBreakPunch, null, [(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape BreakInAsBreakOut = new(RuleAction.BreakInIsBreakOut, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual)]);
    private static readonly Shape BreakOutAsBreakIn = new(RuleAction.BreakOutIsBreakIn, null, [(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual)]);

    private static readonly Shape[] All = [LongBreak, LateBreak, EarlyBreak, NoBreak, ShortHours, LateCheckIn, EarlyCheckOut, Overtime, UnscheduledWork, NoShow, MissingCheckOut, ShortBreak, ReadGap, LoneCheckOut, LoneCheckIn, MorningPair, EveningPair, EveningLast, EveningInThenOut, MorningLast, EveningInThenOutLast, MorningOutThenIn, RepeatCheckInFirst, RepeatCheckInLast, RepeatCheckOutFirst, RepeatCheckOutLast, RepeatBreakPunchFirst, BreakInAsBreakOut, BreakOutAsBreakIn];

    /// <summary>Reads the rules as settings, or returns null and says why they are not the standard rules.</summary>
    public static RuleSettings? TryRead(IReadOnlyList<RuleDto> rules, out string? reason)
    {
        reason = null;
        if (Find(rules) is not { } found)
        {
            reason = NotStandard;
            return null;
        }

        return new RuleSettings
        {
            LongBreakRaises = found[LongBreak].IsEnabled,
            LongBreakMinutesOver = ValueOf(found[LongBreak], RuleFact.MinutesOver),
            LateBreakRaises = found[LateBreak].IsEnabled,
            LateBreakMinutesOver = ValueOf(found[LateBreak], RuleFact.MinutesOver),
            EarlyBreakRaises = found[EarlyBreak].IsEnabled,
            EarlyBreakMinutesOver = ValueOf(found[EarlyBreak], RuleFact.MinutesOver),
            NoBreakRaises = found[NoBreak].IsEnabled,
            ShortHoursRaises = found[ShortHours].IsEnabled,
            ShortHoursMinutesOver = ValueOf(found[ShortHours], RuleFact.MinutesOver),
            LateCheckInRaises = found[LateCheckIn].IsEnabled,
            LateCheckInMinutesOver = ValueOf(found[LateCheckIn], RuleFact.MinutesOver),
            EarlyCheckOutRaises = found[EarlyCheckOut].IsEnabled,
            EarlyCheckOutMinutesOver = ValueOf(found[EarlyCheckOut], RuleFact.MinutesOver),
            OvertimeRaises = found[Overtime].IsEnabled,
            OvertimeMinutesOver = ValueOf(found[Overtime], RuleFact.MinutesOver),
            UnscheduledWorkRaises = found[UnscheduledWork].IsEnabled,
            NoShowRaises = found[NoShow].IsEnabled,
            NoShowWaitMinutes = ValueOf(found[NoShow], RuleFact.MinutesAfterShiftEnd),
            MissingCheckOutWaitMinutes = ValueOf(found[MissingCheckOut], RuleFact.MinutesAfterShiftEnd),
            ShortBreakIsWork = found[ShortBreak].IsEnabled,
            ShortBreakMinutes = ValueOf(found[ShortBreak], RuleFact.BreakMinutes),
            GapIsBreak = found[ReadGap].IsEnabled,
            GapWindowMinutes = ValueOf(found[ReadGap], RuleFact.GapStartOffsetMinutes),
            GapMaxMinutes = ValueOf(found[ReadGap], RuleFact.GapMinutes),
            LoneCheckOutIsCheckIn = found[LoneCheckOut].IsEnabled,
            LoneCheckOutWindowMinutes = ValueOf(found[LoneCheckOut], RuleFact.PunchOffsetMinutes),
            LoneCheckInIsCheckOut = found[LoneCheckIn].IsEnabled,
            LoneCheckInWindowMinutes = ValueOf(found[LoneCheckIn], RuleFact.PunchOffsetMinutes),
            MorningDuplicateIsOne = found[MorningPair].IsEnabled,
            MorningDuplicateWindowMinutes = ValueOf(found[MorningPair], RuleFact.PunchOffsetMinutes),
            MorningDuplicateGapMinutes = ValueOf(found[MorningPair], RuleFact.PunchGapMinutes),
            EveningDuplicateIsOne = found[EveningPair].IsEnabled,
            EveningDuplicateWindowMinutes = ValueOf(found[EveningPair], RuleFact.PunchOffsetMinutes),
            EveningDuplicateGapMinutes = ValueOf(found[EveningPair], RuleFact.PunchGapMinutes),
            EveningLastPunchCounts = found[EveningLast].IsEnabled,
            EveningLastPunchWindowMinutes = ValueOf(found[EveningLast], RuleFact.PunchOffsetMinutes),
            EveningLastPunchGapMinutes = ValueOf(found[EveningLast], RuleFact.PunchGapMinutes),
            EveningInThenOutIsOne = found[EveningInThenOut].IsEnabled,
            EveningInThenOutWindowMinutes = ValueOf(found[EveningInThenOut], RuleFact.PunchOffsetMinutes),
            EveningInThenOutGapMinutes = ValueOf(found[EveningInThenOut], RuleFact.PunchGapMinutes),
            MorningLastPunchCounts = found[MorningLast].IsEnabled,
            MorningLastPunchWindowMinutes = ValueOf(found[MorningLast], RuleFact.PunchOffsetMinutes),
            MorningLastPunchGapMinutes = ValueOf(found[MorningLast], RuleFact.PunchGapMinutes),
            EveningInThenOutLast = found[EveningInThenOutLast].IsEnabled,
            EveningInThenOutLastWindowMinutes = ValueOf(found[EveningInThenOutLast], RuleFact.PunchOffsetMinutes),
            EveningInThenOutLastGapMinutes = ValueOf(found[EveningInThenOutLast], RuleFact.PunchGapMinutes),
            MorningOutThenInCounts = found[MorningOutThenIn].IsEnabled,
            MorningOutThenInWindowMinutes = ValueOf(found[MorningOutThenIn], RuleFact.PunchOffsetMinutes),
            MorningOutThenInGapMinutes = ValueOf(found[MorningOutThenIn], RuleFact.PunchGapMinutes),
            RepeatedCheckInFirst = found[RepeatCheckInFirst].IsEnabled,
            RepeatedCheckInLast = found[RepeatCheckInLast].IsEnabled,
            RepeatedCheckOutFirst = found[RepeatCheckOutFirst].IsEnabled,
            RepeatedCheckOutLast = found[RepeatCheckOutLast].IsEnabled,
            RepeatedBreakPunchFirst = found[RepeatBreakPunchFirst].IsEnabled,
            RepeatedGapMinutes = ValueOf(found[RepeatCheckInFirst], RuleFact.PunchGapMinutes),
            BreakInIsBreakOut = found[BreakInAsBreakOut].IsEnabled,
            BreakInIsBreakOutWindowMinutes = ValueOf(found[BreakInAsBreakOut], RuleFact.PunchOffsetMinutes),
            BreakOutIsBreakIn = found[BreakOutAsBreakIn].IsEnabled,
            BreakOutIsBreakInWindowMinutes = ValueOf(found[BreakOutAsBreakIn], RuleFact.PunchOffsetMinutes),
        };
    }

    /// <summary>Checks the numbers. Errors are keyed <c>Settings.Field</c>, so a form can show each by its field.</summary>
    public static Dictionary<string, string> Validate(RuleSettings settings)
    {
        var errors = new Dictionary<string, string>();
        void Minutes(string field, int value)
        {
            if (value is < 0 or > MaxMinutes)
            {
                errors[$"Settings.{field}"] = $"Use between 0 and {MaxMinutes} minutes.";
            }
        }

        Minutes(nameof(RuleSettings.LongBreakMinutesOver), settings.LongBreakMinutesOver);
        Minutes(nameof(RuleSettings.LateBreakMinutesOver), settings.LateBreakMinutesOver);
        Minutes(nameof(RuleSettings.EarlyBreakMinutesOver), settings.EarlyBreakMinutesOver);
        Minutes(nameof(RuleSettings.ShortHoursMinutesOver), settings.ShortHoursMinutesOver);
        Minutes(nameof(RuleSettings.LateCheckInMinutesOver), settings.LateCheckInMinutesOver);
        Minutes(nameof(RuleSettings.EarlyCheckOutMinutesOver), settings.EarlyCheckOutMinutesOver);
        Minutes(nameof(RuleSettings.OvertimeMinutesOver), settings.OvertimeMinutesOver);
        Minutes(nameof(RuleSettings.NoShowWaitMinutes), settings.NoShowWaitMinutes);
        Minutes(nameof(RuleSettings.MissingCheckOutWaitMinutes), settings.MissingCheckOutWaitMinutes);
        Minutes(nameof(RuleSettings.ShortBreakMinutes), settings.ShortBreakMinutes);
        Minutes(nameof(RuleSettings.GapWindowMinutes), settings.GapWindowMinutes);
        Minutes(nameof(RuleSettings.GapMaxMinutes), settings.GapMaxMinutes);
        Minutes(nameof(RuleSettings.LoneCheckOutWindowMinutes), settings.LoneCheckOutWindowMinutes);
        Minutes(nameof(RuleSettings.LoneCheckInWindowMinutes), settings.LoneCheckInWindowMinutes);
        Minutes(nameof(RuleSettings.MorningDuplicateWindowMinutes), settings.MorningDuplicateWindowMinutes);
        Minutes(nameof(RuleSettings.MorningDuplicateGapMinutes), settings.MorningDuplicateGapMinutes);
        Minutes(nameof(RuleSettings.EveningDuplicateWindowMinutes), settings.EveningDuplicateWindowMinutes);
        Minutes(nameof(RuleSettings.EveningDuplicateGapMinutes), settings.EveningDuplicateGapMinutes);
        Minutes(nameof(RuleSettings.EveningLastPunchWindowMinutes), settings.EveningLastPunchWindowMinutes);
        Minutes(nameof(RuleSettings.EveningLastPunchGapMinutes), settings.EveningLastPunchGapMinutes);
        Minutes(nameof(RuleSettings.EveningInThenOutWindowMinutes), settings.EveningInThenOutWindowMinutes);
        Minutes(nameof(RuleSettings.EveningInThenOutGapMinutes), settings.EveningInThenOutGapMinutes);
        Minutes(nameof(RuleSettings.MorningLastPunchWindowMinutes), settings.MorningLastPunchWindowMinutes);
        Minutes(nameof(RuleSettings.MorningLastPunchGapMinutes), settings.MorningLastPunchGapMinutes);
        Minutes(nameof(RuleSettings.EveningInThenOutLastWindowMinutes), settings.EveningInThenOutLastWindowMinutes);
        Minutes(nameof(RuleSettings.EveningInThenOutLastGapMinutes), settings.EveningInThenOutLastGapMinutes);
        Minutes(nameof(RuleSettings.MorningOutThenInWindowMinutes), settings.MorningOutThenInWindowMinutes);
        Minutes(nameof(RuleSettings.MorningOutThenInGapMinutes), settings.MorningOutThenInGapMinutes);
        Minutes(nameof(RuleSettings.RepeatedGapMinutes), settings.RepeatedGapMinutes);
        Minutes(nameof(RuleSettings.BreakInIsBreakOutWindowMinutes), settings.BreakInIsBreakOutWindowMinutes);
        Minutes(nameof(RuleSettings.BreakOutIsBreakInWindowMinutes), settings.BreakOutIsBreakInWindowMinutes);

        // Two repeated check-ins (or check-outs) keep the first or the last, not both.
        if (settings.RepeatedCheckInFirst && settings.RepeatedCheckInLast)
        {
            errors[$"Settings.{nameof(RuleSettings.RepeatedCheckInLast)}"] = "Repeated check-ins can keep the first or keep the last, not both. Switch one off.";
        }

        if (settings.RepeatedCheckOutFirst && settings.RepeatedCheckOutLast)
        {
            errors[$"Settings.{nameof(RuleSettings.RepeatedCheckOutLast)}"] = "Repeated check-outs can keep the first or keep the last, not both. Switch one off.";
        }

        // A check-in then a check-out at the end is read one way or the other: the rule that reads the check-in as the check-out would take the pair first.
        if (settings.EveningInThenOutIsOne && settings.EveningInThenOutLast)
        {
            errors[$"Settings.{nameof(RuleSettings.EveningInThenOutLast)}"] = "A check-in then a check-out at the end can be read as the check-out or leave the check-in out, not both. Switch one off.";
        }

        // Of two close check-outs (or check-ins) the day keeps the first or the last, not both: the rule that keeps the first would read the pair before the other could.
        if (settings.EveningDuplicateIsOne && settings.EveningLastPunchCounts)
        {
            errors[$"Settings.{nameof(RuleSettings.EveningLastPunchCounts)}"] = "Two close check-outs at the end can keep the first or keep the last, not both. Switch one off.";
        }

        if (settings.MorningDuplicateIsOne && settings.MorningLastPunchCounts)
        {
            errors[$"Settings.{nameof(RuleSettings.MorningLastPunchCounts)}"] = "Two close check-ins at the start can keep the first or keep the last, not both. Switch one off.";
        }

        return errors;
    }

    /// <summary>
    /// The standard rules with the settings written into them, as new rules (the ones given are not changed), or null when the rules are not
    /// standard. A missing check-out is always raised in the end, so its rule stays on and only its wait changes.
    /// </summary>
    public static List<RuleDto>? Apply(IReadOnlyList<RuleDto> rules, RuleSettings settings)
    {
        var copy = rules.Select(Clone).ToList();
        if (Find(copy) is not { } found)
        {
            return null;
        }

        Set(found[LongBreak], settings.LongBreakRaises, RuleFact.MinutesOver, settings.LongBreakMinutesOver);
        Set(found[LateBreak], settings.LateBreakRaises, RuleFact.MinutesOver, settings.LateBreakMinutesOver);
        Set(found[EarlyBreak], settings.EarlyBreakRaises, RuleFact.MinutesOver, settings.EarlyBreakMinutesOver);
        found[NoBreak].IsEnabled = settings.NoBreakRaises;
        Set(found[ShortHours], settings.ShortHoursRaises, RuleFact.MinutesOver, settings.ShortHoursMinutesOver);
        Set(found[LateCheckIn], settings.LateCheckInRaises, RuleFact.MinutesOver, settings.LateCheckInMinutesOver);
        Set(found[EarlyCheckOut], settings.EarlyCheckOutRaises, RuleFact.MinutesOver, settings.EarlyCheckOutMinutesOver);
        Set(found[Overtime], settings.OvertimeRaises, RuleFact.MinutesOver, settings.OvertimeMinutesOver);
        found[UnscheduledWork].IsEnabled = settings.UnscheduledWorkRaises;
        Set(found[NoShow], settings.NoShowRaises, RuleFact.MinutesAfterShiftEnd, settings.NoShowWaitMinutes);
        Set(found[MissingCheckOut], true, RuleFact.MinutesAfterShiftEnd, settings.MissingCheckOutWaitMinutes);
        Set(found[ShortBreak], settings.ShortBreakIsWork, RuleFact.BreakMinutes, settings.ShortBreakMinutes);
        Set(found[ReadGap], settings.GapIsBreak, RuleFact.GapStartOffsetMinutes, settings.GapWindowMinutes);
        SetValue(found[ReadGap], RuleFact.GapMinutes, settings.GapMaxMinutes);
        Set(found[LoneCheckOut], settings.LoneCheckOutIsCheckIn, RuleFact.PunchOffsetMinutes, settings.LoneCheckOutWindowMinutes);
        Set(found[LoneCheckIn], settings.LoneCheckInIsCheckOut, RuleFact.PunchOffsetMinutes, settings.LoneCheckInWindowMinutes);
        Set(found[MorningPair], settings.MorningDuplicateIsOne, RuleFact.PunchOffsetMinutes, settings.MorningDuplicateWindowMinutes);
        SetValue(found[MorningPair], RuleFact.PunchGapMinutes, settings.MorningDuplicateGapMinutes);
        Set(found[EveningPair], settings.EveningDuplicateIsOne, RuleFact.PunchOffsetMinutes, settings.EveningDuplicateWindowMinutes);
        SetValue(found[EveningPair], RuleFact.PunchGapMinutes, settings.EveningDuplicateGapMinutes);
        Set(found[EveningLast], settings.EveningLastPunchCounts, RuleFact.PunchOffsetMinutes, settings.EveningLastPunchWindowMinutes);
        SetValue(found[EveningLast], RuleFact.PunchGapMinutes, settings.EveningLastPunchGapMinutes);
        Set(found[EveningInThenOut], settings.EveningInThenOutIsOne, RuleFact.PunchOffsetMinutes, settings.EveningInThenOutWindowMinutes);
        SetValue(found[EveningInThenOut], RuleFact.PunchGapMinutes, settings.EveningInThenOutGapMinutes);
        Set(found[MorningLast], settings.MorningLastPunchCounts, RuleFact.PunchOffsetMinutes, settings.MorningLastPunchWindowMinutes);
        SetValue(found[MorningLast], RuleFact.PunchGapMinutes, settings.MorningLastPunchGapMinutes);
        Set(found[EveningInThenOutLast], settings.EveningInThenOutLast, RuleFact.PunchOffsetMinutes, settings.EveningInThenOutLastWindowMinutes);
        SetValue(found[EveningInThenOutLast], RuleFact.PunchGapMinutes, settings.EveningInThenOutLastGapMinutes);
        Set(found[MorningOutThenIn], settings.MorningOutThenInCounts, RuleFact.PunchOffsetMinutes, settings.MorningOutThenInWindowMinutes);
        SetValue(found[MorningOutThenIn], RuleFact.PunchGapMinutes, settings.MorningOutThenInGapMinutes);
        Set(found[RepeatCheckInFirst], settings.RepeatedCheckInFirst, RuleFact.PunchGapMinutes, settings.RepeatedGapMinutes);
        Set(found[RepeatCheckInLast], settings.RepeatedCheckInLast, RuleFact.PunchGapMinutes, settings.RepeatedGapMinutes);
        Set(found[RepeatCheckOutFirst], settings.RepeatedCheckOutFirst, RuleFact.PunchGapMinutes, settings.RepeatedGapMinutes);
        Set(found[RepeatCheckOutLast], settings.RepeatedCheckOutLast, RuleFact.PunchGapMinutes, settings.RepeatedGapMinutes);
        Set(found[RepeatBreakPunchFirst], settings.RepeatedBreakPunchFirst, RuleFact.PunchGapMinutes, settings.RepeatedGapMinutes);
        Set(found[BreakInAsBreakOut], settings.BreakInIsBreakOut, RuleFact.PunchOffsetMinutes, settings.BreakInIsBreakOutWindowMinutes);
        Set(found[BreakOutAsBreakIn], settings.BreakOutIsBreakIn, RuleFact.PunchOffsetMinutes, settings.BreakOutIsBreakInWindowMinutes);
        foreach (var rule in copy)
        {
            rule.Sentence = RuleSentence.Describe(rule);
        }

        return copy;
    }

    /// <summary>What changed from one settings to the next, in words, for the history and the audit log; empty when nothing did.</summary>
    public static string Describe(RuleSettings before, RuleSettings after)
    {
        var changes = new List<string>();
        void Switch(string label, bool was, bool now)
        {
            if (was != now)
            {
                changes.Add($"{label} {(now ? "on" : "off")}");
            }
        }

        void Number(string label, int was, int now)
        {
            if (was != now)
            {
                changes.Add($"{label} {was} → {now} min");
            }
        }

        Switch("long break", before.LongBreakRaises, after.LongBreakRaises);
        Number("long break over", before.LongBreakMinutesOver, after.LongBreakMinutesOver);
        Switch("late break", before.LateBreakRaises, after.LateBreakRaises);
        Number("late break over", before.LateBreakMinutesOver, after.LateBreakMinutesOver);
        Switch("early break", before.EarlyBreakRaises, after.EarlyBreakRaises);
        Number("early break over", before.EarlyBreakMinutesOver, after.EarlyBreakMinutesOver);
        Switch("no break", before.NoBreakRaises, after.NoBreakRaises);
        Switch("day worked short", before.ShortHoursRaises, after.ShortHoursRaises);
        Number("day worked short over", before.ShortHoursMinutesOver, after.ShortHoursMinutesOver);
        Switch("late check-in", before.LateCheckInRaises, after.LateCheckInRaises);
        Number("late check-in over", before.LateCheckInMinutesOver, after.LateCheckInMinutesOver);
        Switch("early check-out", before.EarlyCheckOutRaises, after.EarlyCheckOutRaises);
        Number("early check-out over", before.EarlyCheckOutMinutesOver, after.EarlyCheckOutMinutesOver);
        Switch("overtime", before.OvertimeRaises, after.OvertimeRaises);
        Number("overtime over", before.OvertimeMinutesOver, after.OvertimeMinutesOver);
        Switch("work with no scheduled shift", before.UnscheduledWorkRaises, after.UnscheduledWorkRaises);
        Switch("no-show", before.NoShowRaises, after.NoShowRaises);
        Number("no-show wait", before.NoShowWaitMinutes, after.NoShowWaitMinutes);
        Number("missing check-out wait", before.MissingCheckOutWaitMinutes, after.MissingCheckOutWaitMinutes);
        Switch("short break counts as work", before.ShortBreakIsWork, after.ShortBreakIsWork);
        Number("short break under", before.ShortBreakMinutes, after.ShortBreakMinutes);
        Switch("gap read as the break", before.GapIsBreak, after.GapIsBreak);
        Number("gap window", before.GapWindowMinutes, after.GapWindowMinutes);
        Number("longest gap", before.GapMaxMinutes, after.GapMaxMinutes);
        Switch("lone check-out read as the check-in", before.LoneCheckOutIsCheckIn, after.LoneCheckOutIsCheckIn);
        Number("lone check-out within", before.LoneCheckOutWindowMinutes, after.LoneCheckOutWindowMinutes);
        Switch("lone check-in read as the check-out", before.LoneCheckInIsCheckOut, after.LoneCheckInIsCheckOut);
        Number("lone check-in within", before.LoneCheckInWindowMinutes, after.LoneCheckInWindowMinutes);
        Switch("two close punches at the start count as one", before.MorningDuplicateIsOne, after.MorningDuplicateIsOne);
        Number("start punches within", before.MorningDuplicateWindowMinutes, after.MorningDuplicateWindowMinutes);
        Number("start punches apart", before.MorningDuplicateGapMinutes, after.MorningDuplicateGapMinutes);
        Switch("two close check-outs at the end count as one", before.EveningDuplicateIsOne, after.EveningDuplicateIsOne);
        Number("end punches within", before.EveningDuplicateWindowMinutes, after.EveningDuplicateWindowMinutes);
        Number("end punches apart", before.EveningDuplicateGapMinutes, after.EveningDuplicateGapMinutes);
        Switch("two close check-outs at the end count as the last one", before.EveningLastPunchCounts, after.EveningLastPunchCounts);
        Number("last check-outs within", before.EveningLastPunchWindowMinutes, after.EveningLastPunchWindowMinutes);
        Number("last check-outs apart", before.EveningLastPunchGapMinutes, after.EveningLastPunchGapMinutes);
        Switch("a check-in then a check-out at the end count as one check-out", before.EveningInThenOutIsOne, after.EveningInThenOutIsOne);
        Number("check-in then check-out within", before.EveningInThenOutWindowMinutes, after.EveningInThenOutWindowMinutes);
        Number("check-in then check-out apart", before.EveningInThenOutGapMinutes, after.EveningInThenOutGapMinutes);
        Switch("two close check-ins at the start count as the last one", before.MorningLastPunchCounts, after.MorningLastPunchCounts);
        Number("last check-ins within", before.MorningLastPunchWindowMinutes, after.MorningLastPunchWindowMinutes);
        Number("last check-ins apart", before.MorningLastPunchGapMinutes, after.MorningLastPunchGapMinutes);
        Switch("a check-in then a check-out at the end keep the check-out", before.EveningInThenOutLast, after.EveningInThenOutLast);
        Number("kept check-out within", before.EveningInThenOutLastWindowMinutes, after.EveningInThenOutLastWindowMinutes);
        Number("check-in then check-out apart (keeping the check-out)", before.EveningInThenOutLastGapMinutes, after.EveningInThenOutLastGapMinutes);
        Switch("a check-out then a check-in at the start keep the check-in", before.MorningOutThenInCounts, after.MorningOutThenInCounts);
        Number("kept check-in within", before.MorningOutThenInWindowMinutes, after.MorningOutThenInWindowMinutes);
        Number("check-out then check-in apart", before.MorningOutThenInGapMinutes, after.MorningOutThenInGapMinutes);
        Switch("repeated check-ins keep the first", before.RepeatedCheckInFirst, after.RepeatedCheckInFirst);
        Switch("repeated check-ins keep the last", before.RepeatedCheckInLast, after.RepeatedCheckInLast);
        Switch("repeated check-outs keep the first", before.RepeatedCheckOutFirst, after.RepeatedCheckOutFirst);
        Switch("repeated check-outs keep the last", before.RepeatedCheckOutLast, after.RepeatedCheckOutLast);
        Switch("repeated break punches keep the first", before.RepeatedBreakPunchFirst, after.RepeatedBreakPunchFirst);
        Number("repeated punches apart", before.RepeatedGapMinutes, after.RepeatedGapMinutes);
        Switch("break-in read as the break-out", before.BreakInIsBreakOut, after.BreakInIsBreakOut);
        Number("break-in within", before.BreakInIsBreakOutWindowMinutes, after.BreakInIsBreakOutWindowMinutes);
        Switch("break-out read as the break-in", before.BreakOutIsBreakIn, after.BreakOutIsBreakIn);
        Number("break-out within", before.BreakOutIsBreakInWindowMinutes, after.BreakOutIsBreakInWindowMinutes);
        return changes.Count == 0 ? string.Empty : "Settings: " + string.Join(", ", changes) + ".";
    }

    /// <summary>Finds each standard rule once, or null when the rules are not exactly the standard ones.</summary>
    private static Dictionary<Shape, RuleDto>? Find(IReadOnlyList<RuleDto> rules)
    {
        if (rules.Count != All.Length)
        {
            return null;
        }

        var found = new Dictionary<Shape, RuleDto>();
        foreach (var shape in All)
        {
            var matches = rules.Where(shape.Matches).ToList();
            if (matches.Count != 1)
            {
                return null;
            }

            found[shape] = matches[0];
        }

        // The usual condition of reading a gap says a shift has a break; it is not a setting, so it has to be as usual.
        return found[ReadGap].Conditions.Any(c => c.Fact == nameof(RuleFact.ShiftHasBreak) && c.Value != 1) ? null : found;
    }

    private static void Set(RuleDto rule, bool enabled, RuleFact fact, int value)
    {
        rule.IsEnabled = enabled;
        SetValue(rule, fact, value);
    }

    private static void SetValue(RuleDto rule, RuleFact fact, int value) =>
        rule.Conditions.Single(c => c.Fact == fact.ToString()).Value = value;

    private static int ValueOf(RuleDto rule, RuleFact fact) =>
        rule.Conditions.Single(c => c.Fact == fact.ToString()).Value;

    private static RuleDto Clone(RuleDto rule) => new()
    {
        Name = rule.Name,
        IsEnabled = rule.IsEnabled,
        Stage = rule.Stage,
        Action = rule.Action,
        Finding = rule.Finding,
        Reason = rule.Reason,
        Sentence = rule.Sentence,
        Conditions = rule.Conditions.Select(c => new ConditionDto { Fact = c.Fact, Operator = c.Operator, Value = c.Value }).ToList(),
    };

    /// <summary>One standard rule: what it does, what it is about, and the facts and operators of its conditions, in any order.</summary>
    private sealed record Shape(RuleAction Action, RuleFinding? Finding, (RuleFact Fact, RuleOperator Operator)[] Conditions)
    {
        public bool Matches(RuleDto rule) =>
            rule.Action == Action.ToString()
            && (rule.Finding ?? string.Empty) == (Finding?.ToString() ?? string.Empty)
            && rule.Conditions.Count == Conditions.Length
            && Conditions.All(expected => rule.Conditions.Count(c => c.Fact == expected.Fact.ToString() && c.Operator == expected.Operator.ToString()) == 1);
    }
}
