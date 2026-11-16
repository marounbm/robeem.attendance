using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Reads the standard rules of a scope as <see cref="RuleSettings"/> and writes settings back into them. The standard rules are the eight the
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

    private static readonly Shape[] All = [LongBreak, LateBreak, EarlyBreak, NoBreak, NoShow, MissingCheckOut, ShortBreak, ReadGap];

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
            NoShowRaises = found[NoShow].IsEnabled,
            NoShowWaitMinutes = ValueOf(found[NoShow], RuleFact.MinutesAfterShiftEnd),
            MissingCheckOutWaitMinutes = ValueOf(found[MissingCheckOut], RuleFact.MinutesAfterShiftEnd),
            ShortBreakIsWork = found[ShortBreak].IsEnabled,
            ShortBreakMinutes = ValueOf(found[ShortBreak], RuleFact.BreakMinutes),
            GapIsBreak = found[ReadGap].IsEnabled,
            GapWindowMinutes = ValueOf(found[ReadGap], RuleFact.GapStartOffsetMinutes),
            GapMaxMinutes = ValueOf(found[ReadGap], RuleFact.GapMinutes),
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
        Minutes(nameof(RuleSettings.NoShowWaitMinutes), settings.NoShowWaitMinutes);
        Minutes(nameof(RuleSettings.MissingCheckOutWaitMinutes), settings.MissingCheckOutWaitMinutes);
        Minutes(nameof(RuleSettings.ShortBreakMinutes), settings.ShortBreakMinutes);
        Minutes(nameof(RuleSettings.GapWindowMinutes), settings.GapWindowMinutes);
        Minutes(nameof(RuleSettings.GapMaxMinutes), settings.GapMaxMinutes);
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
        Set(found[NoShow], settings.NoShowRaises, RuleFact.MinutesAfterShiftEnd, settings.NoShowWaitMinutes);
        Set(found[MissingCheckOut], true, RuleFact.MinutesAfterShiftEnd, settings.MissingCheckOutWaitMinutes);
        Set(found[ShortBreak], settings.ShortBreakIsWork, RuleFact.BreakMinutes, settings.ShortBreakMinutes);
        Set(found[ReadGap], settings.GapIsBreak, RuleFact.GapStartOffsetMinutes, settings.GapWindowMinutes);
        SetValue(found[ReadGap], RuleFact.GapMinutes, settings.GapMaxMinutes);
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
        Switch("no-show", before.NoShowRaises, after.NoShowRaises);
        Number("no-show wait", before.NoShowWaitMinutes, after.NoShowWaitMinutes);
        Number("missing check-out wait", before.MissingCheckOutWaitMinutes, after.MissingCheckOutWaitMinutes);
        Switch("short break counts as work", before.ShortBreakIsWork, after.ShortBreakIsWork);
        Number("short break under", before.ShortBreakMinutes, after.ShortBreakMinutes);
        Switch("gap read as the break", before.GapIsBreak, after.GapIsBreak);
        Number("gap window", before.GapWindowMinutes, after.GapWindowMinutes);
        Number("longest gap", before.GapMaxMinutes, after.GapMaxMinutes);
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
