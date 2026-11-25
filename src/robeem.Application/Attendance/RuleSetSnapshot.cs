using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The rules in force for one policy scope, and the questions the calculation asks them. Pure: no I/O. With no published rule set the
/// rules are <see cref="FromPolicy"/>: the policy's own settings written as rules, so nothing changes until a set is published.
/// </summary>
public sealed class RuleSetSnapshot
{
    /// <summary>How long after a shift ends a missing check-out is raised at the latest, whatever the rules say: 24 hours.</summary>
    public const int MissingCheckOutBackstopMinutes = 1440;

    /// <summary>How far from the start or end of the shift a punch may be and still be fixed by the automatic fixes, to begin with: 2 hours.</summary>
    public const int DefaultWindowMinutes = 120;

    /// <summary>How many minutes apart two punches may be and still be one punch said twice, to begin with.</summary>
    public const int DefaultGapMinutes = 10;

    /// <summary>How far from the scheduled start or end of the break a break punch may be and still be fixed by the break fixes, to begin with: 30 minutes.</summary>
    public const int DefaultBreakWindowMinutes = 30;

    public RuleSetSnapshot(int version, string source, IReadOnlyList<RuleSnapshot> rules)
    {
        Version = version;
        Source = source;
        Rules = rules.OrderBy(r => r.Priority).ToList();
    }

    /// <summary>Gets the number of the published set; 0 when the rules come from the policy.</summary>
    public int Version { get; }

    /// <summary>Gets where the rules come from, for display: "the policy" or "published version N".</summary>
    public string Source { get; }

    public IReadOnlyList<RuleSnapshot> Rules { get; }

    /// <summary>Gets a value indicating whether any enabled rule can read a gap as the break.</summary>
    public bool CanInferBreak => Rules.Any(r => r.IsEnabled && r.Action == RuleAction.InferBreak);

    /// <summary>Gets a value indicating whether any enabled rule fixes punches, so the calculation has to look for them.</summary>
    public bool CanFixPunches => Rules.Any(r => r.IsEnabled && IsAutoFix(r.Action));

    /// <summary>Whether the action is one of the five that read two close punches of the same kind anywhere in the day.</summary>
    public static bool IsRepeatFix(RuleAction action) =>
        action is RuleAction.KeepFirstRepeatedCheckIn or RuleAction.KeepLastRepeatedCheckIn or RuleAction.KeepFirstRepeatedCheckOut or RuleAction.KeepLastRepeatedCheckOut or RuleAction.KeepFirstRepeatedBreakPunch;

    /// <summary>Whether the action is one of the sixteen that fix a day's punches.</summary>
    public static bool IsAutoFix(RuleAction action) =>
        IsCheckInOutFix(action) || IsBreakFix(action) || IsRepeatFix(action)
        || action is RuleAction.KeepLastEveningPunch or RuleAction.KeepFirstEveningCheckInPunch or RuleAction.KeepLastMorningPunch or RuleAction.KeepEveningCheckOutPunch or RuleAction.KeepMorningCheckInPunch;

    /// <summary>Whether the action is one of the two that read a break punch of the wrong type.</summary>
    public static bool IsBreakFix(RuleAction action) => action is RuleAction.BreakInIsBreakOut or RuleAction.BreakOutIsBreakIn;

    /// <summary>Whether the action is one of the four that read a check-in or check-out, or leave a repeated one out.</summary>
    public static bool IsCheckInOutFix(RuleAction action) =>
        action is RuleAction.LoneCheckOutIsCheckIn or RuleAction.LoneCheckInIsCheckOut or RuleAction.KeepFirstMorningPunch or RuleAction.KeepFirstEveningPunch;

    /// <summary>
    /// The policy's settings as rules: one to read the break from a gap (on when the policy's break inference is), and one to raise each
    /// finding the policy raises. The numbers that decide whether there is a finding at all (how far over a break may go, how far from its
    /// scheduled start, how long a no-show waits) are written into those rules as conditions, so they are changed in the rules.
    /// </summary>
    public static RuleSetSnapshot FromPolicy(AttendancePolicy policy)
    {
        var rules = new List<RuleSnapshot>
        {
            new(
                "Read the break from the gap near it",
                10,
                policy.BreakInferenceMode == BreakInferenceMode.FromGap,
                RuleStage.Interpretation,
                RuleAction.InferBreak,
                null,
                null,
                [
                    new ConditionSnapshot(RuleFact.ShiftHasBreak, RuleOperator.Equals, 1),
                    new ConditionSnapshot(RuleFact.GapStartOffsetMinutes, RuleOperator.LessOrEqual, policy.InferredBreakWindowMinutes),
                    new ConditionSnapshot(RuleFact.GapMinutes, RuleOperator.LessOrEqual, policy.InferredBreakMaxMinutes),
                    new ConditionSnapshot(RuleFact.GapExcusedPercent, RuleOperator.LessOrEqual, 50),
                ]),
            Raise("Raise an exception for a long break", 20, RuleFinding.LongBreak, policy.RaiseExceptionOnLongBreak, policy),
            Raise("Raise an exception for a late break", 30, RuleFinding.LateBreak, policy.RaiseExceptionOnLateBreak, policy),
            Raise("Raise an exception for an early break", 40, RuleFinding.EarlyBreak, policy.RaiseExceptionOnEarlyBreak, policy),
            Raise("Raise an exception when no break was taken", 50, RuleFinding.NoBreak, policy.RaiseExceptionOnNoBreak, policy),
            Raise("Raise an exception when less than the scheduled time was worked", 55, RuleFinding.ShortHours, true, policy),
            Raise("Raise an exception for a late check-in", 56, RuleFinding.LateCheckIn, false, policy),
            Raise("Raise an exception for an early check-out", 57, RuleFinding.EarlyCheckOut, false, policy),
            Raise("Raise an exception for overtime", 58, RuleFinding.Overtime, false, policy),
            Raise("Raise an exception for work with no scheduled shift", 59, RuleFinding.UnscheduledWork, false, policy),
            Raise("Raise an exception for a no-show", 60, RuleFinding.NoShow, true, policy),
            Raise("Raise an exception for a missing check-out", 65, RuleFinding.MissingCheckOut, true, policy),
            ShortBreak(70, policy),
        };
        rules.AddRange(AutoFixes(80));
        rules.AddRange(BreakFixes(84));
        rules.Add(KeepLast(86));
        rules.AddRange(PunchPairs(87));
        rules.AddRange(CheckPairs(89));
        rules.AddRange(Repeats(91));

        return new RuleSetSnapshot(0, "the policy", rules);
    }

    /// <summary>
    /// The same rules with the five rules for repeated punches added, all off, when there are none. It is how a set made before they existed
    /// (<see cref="AttendanceRuleSet.RepeatsInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithRepeats()
    {
        var added = Repeats(int.MaxValue - 40)
            .Where(add => !Rules.Any(r => r.Action == add.Action))
            .ToList();
        return added.Count == 0 ? this : new RuleSetSnapshot(Version, Source, [.. Rules, .. added]);
    }

    /// <summary>
    /// The same rules with the rule that keeps the check-out of a check-in then a check-out at the end, and the rule that keeps the check-in of a check-out then a
    /// check-in at the start, added, both off, when there are none. It is how a set made before they existed (<see cref="AttendanceRuleSet.CheckPairsInRules"/>
    /// false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithCheckPairs()
    {
        var added = CheckPairs(int.MaxValue - 26)
            .Where(add => !Rules.Any(r => r.Action == add.Action))
            .ToList();
        return added.Count == 0 ? this : new RuleSetSnapshot(Version, Source, [.. Rules, .. added]);
    }

    /// <summary>
    /// The same rules with the rule for a check-in then a check-out at the end and the rule that keeps the last of two close check-ins at the start added
    /// when there are none. It is how a set made before they existed (<see cref="AttendanceRuleSet.PunchPairsInRules"/> false) is read: the first copies the
    /// switch and numbers of the rule for two check-outs at the end, which used to cover this case too, so the set behaves as it did when it was published;
    /// the second is off.
    /// </summary>
    public RuleSetSnapshot WithPunchPairs()
    {
        var added = new List<RuleSnapshot>();
        if (!Rules.Any(r => r.Action == RuleAction.KeepFirstEveningCheckInPunch))
        {
            var before = Rules.FirstOrDefault(r => r.Action == RuleAction.KeepFirstEveningPunch);
            var split = AutoFix("Read a check-in then a check-out at the end as one check-out", int.MaxValue - 24, RuleAction.KeepFirstEveningCheckInPunch, DefaultWindowMinutes, DefaultGapMinutes);
            added.Add(before is null ? split : split with { IsEnabled = before.IsEnabled, Conditions = before.Conditions });
        }

        if (!Rules.Any(r => r.Action == RuleAction.KeepLastMorningPunch))
        {
            added.Add(AutoFix("Keep the last of two close check-ins at the start", int.MaxValue - 23, RuleAction.KeepLastMorningPunch, DefaultWindowMinutes, DefaultGapMinutes));
        }

        return added.Count == 0 ? this : new RuleSetSnapshot(Version, Source, [.. Rules, .. added]);
    }

    /// <summary>
    /// The same rules with the rule that keeps the last of two close check-outs added, off, when there is none. It is how a set made before it existed
    /// (<see cref="AttendanceRuleSet.KeepLastInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithKeepLast()
    {
        if (Rules.Any(r => r.Action == RuleAction.KeepLastEveningPunch))
        {
            return this;
        }

        return new RuleSetSnapshot(Version, Source, [.. Rules, KeepLast(int.MaxValue - 22)]);
    }

    /// <summary>
    /// The same rules with the two break fixes added, off, when there are none. It is how a set made before they existed
    /// (<see cref="AttendanceRuleSet.BreakFixesInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithBreakFixes()
    {
        if (Rules.Any(r => IsBreakFix(r.Action)))
        {
            return this;
        }

        return new RuleSetSnapshot(Version, Source, [.. Rules, .. BreakFixes(int.MaxValue - 20)]);
    }

    /// <summary>
    /// The same rules with the rules for a late check-in and an early check-out added, both off, when there are none. It is how a set made before they
    /// were findings (<see cref="AttendanceRuleSet.LateEarlyInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithLateEarly(AttendancePolicy policy)
    {
        var added = new List<RuleSnapshot>();
        if (!Rules.Any(r => r.Finding == RuleFinding.LateCheckIn))
        {
            added.Add(Raise("Raise an exception for a late check-in", int.MaxValue - 4, RuleFinding.LateCheckIn, false, policy));
        }

        if (!Rules.Any(r => r.Finding == RuleFinding.EarlyCheckOut))
        {
            added.Add(Raise("Raise an exception for an early check-out", int.MaxValue - 3, RuleFinding.EarlyCheckOut, false, policy));
        }

        return added.Count == 0 ? this : new RuleSetSnapshot(Version, Source, [.. Rules, .. added]);
    }

    /// <summary>
    /// The same rules with the rules for overtime and for work with no scheduled shift added, both off, when there are none. It is how a set made before
    /// they were findings (<see cref="AttendanceRuleSet.OvertimeInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithOvertime(AttendancePolicy policy)
    {
        var added = new List<RuleSnapshot>();
        if (!Rules.Any(r => r.Finding == RuleFinding.Overtime))
        {
            added.Add(Raise("Raise an exception for overtime", int.MaxValue - 6, RuleFinding.Overtime, false, policy));
        }

        if (!Rules.Any(r => r.Finding == RuleFinding.UnscheduledWork))
        {
            added.Add(Raise("Raise an exception for work with no scheduled shift", int.MaxValue - 5, RuleFinding.UnscheduledWork, false, policy));
        }

        return added.Count == 0 ? this : new RuleSetSnapshot(Version, Source, [.. Rules, .. added]);
    }

    /// <summary>
    /// The same rules with the four automatic punch fixes added, all off, when there are none. It is how a set made before they existed
    /// (<see cref="AttendanceRuleSet.AutoFixesInRules"/> false) is read, so it behaves as it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithAutoFixes()
    {
        if (Rules.Any(r => IsCheckInOutFix(r.Action)))
        {
            return this;
        }

        return new RuleSetSnapshot(Version, Source, [.. Rules, .. AutoFixes(int.MaxValue - 10)]);
    }

    /// <summary>Whether an enabled rule of this action has its conditions met by <paramref name="facts"/>: how the calculation asks whether to fix a day's punches.</summary>
    public bool Applies(RuleAction action, RuleFacts facts) =>
        Rules.Any(r => r.Stage == RuleStage.Interpretation && r.Action == action && r.Matches(facts));

    /// <summary>
    /// The same rules with the policy's thresholds added to the conditions of every exception rule about a break finding or a no-show. It is how a
    /// set published before the thresholds moved into the rules (<see cref="AttendanceRuleSet.ThresholdsInRules"/> false) is read, so it behaves as
    /// it did when it was published.
    /// </summary>
    public RuleSetSnapshot WithPolicyThresholds(AttendancePolicy policy)
    {
        var rules = Rules
            .Select(r => r.Stage == RuleStage.Exception && Threshold(r.Finding, policy) is { } threshold ? r with { Conditions = [.. r.Conditions, threshold] } : r)
            .ToList();

        // Such a set has no rule that counts a short break as work, because the policy decided it: the rule is added, last.
        if (!rules.Any(r => r.Action == RuleAction.IgnoreShortBreak))
        {
            rules.Add(ShortBreak(int.MaxValue, policy));
        }

        // Nor has it a rule for how long a missing check-out waits: the policy's wait is added the same way.
        if (!rules.Any(r => r.Finding == RuleFinding.MissingCheckOut))
        {
            rules.Add(Raise("Raise an exception for a missing check-out", int.MaxValue - 1, RuleFinding.MissingCheckOut, true, policy));
        }

        return new RuleSetSnapshot(Version, Source, rules);
    }

    /// <summary>
    /// The same rules with the rule for a day worked short added when there is none. It is how a set made before that finding existed
    /// (<see cref="AttendanceRuleSet.ShortHoursInRules"/> false) is read: as if it had the policy's own rule, on, so it behaves as the
    /// policy's starting rules do. A set made since carries the rule itself, or leaves it out on purpose, and is not read through this.
    /// </summary>
    public RuleSetSnapshot WithShortHours(AttendancePolicy policy)
    {
        if (Rules.Any(r => r.Finding == RuleFinding.ShortHours))
        {
            return this;
        }

        return new RuleSetSnapshot(Version, Source, [.. Rules, Raise("Raise an exception when less than the scheduled time was worked", int.MaxValue - 2, RuleFinding.ShortHours, true, policy)]);
    }

    /// <summary>
    /// Whether a missing check-out (or an open break at the end of the shift) is raised yet: the rules for it say how long to wait after the shift
    /// ended, but they can only delay it. A missing check-out is raised at the latest <see cref="MissingCheckOutBackstopMinutes"/> after the shift
    /// ended, whatever the rules are, so a day always has the exception it needs to be corrected.
    /// </summary>
    public bool RaisesMissingCheckOut(RuleFacts facts) =>
        facts.MinutesAfterShiftEnd >= MissingCheckOutBackstopMinutes || Raises(RuleFinding.MissingCheckOut, facts);

    /// <summary>
    /// Whether the finding becomes an exception. The rules for the finding are looked at in priority order and the first whose
    /// conditions hold decides: a raise rule raises, a suppress rule does not. With none, nothing is raised.
    /// </summary>
    public bool Raises(RuleFinding finding, RuleFacts facts)
    {
        var decided = Rules.FirstOrDefault(r => r.Stage == RuleStage.Exception && r.Finding == finding && r.Matches(facts));
        return decided is { Action: RuleAction.RaiseException };
    }

    /// <summary>
    /// Whether the finding is worth noting on the day: some rule about it, on or off, has its conditions met. A rule that is off is how a
    /// finding is recorded but not raised. With no rule about the finding, it is not noted.
    /// </summary>
    public bool Notes(RuleFinding finding, RuleFacts facts) =>
        Rules.Any(r => r.Stage == RuleStage.Exception && r.Finding == finding && r.ConditionsHold(facts));

    /// <summary>Whether the gap described by <paramref name="gap"/> is read as the break: the first enabled interpretation rule whose conditions hold decides.</summary>
    public bool InfersBreak(RuleFacts gap) =>
        Rules.Any(r => r.Stage == RuleStage.Interpretation && r.Action == RuleAction.InferBreak && r.Matches(gap));

    /// <summary>Whether a break that lasted as <paramref name="facts"/> says is a short one, counted as work: some enabled rule that counts a short break as work has its conditions met.</summary>
    public bool IgnoresShortBreak(RuleFacts facts) =>
        Rules.Any(r => r.Stage == RuleStage.Interpretation && r.Action == RuleAction.IgnoreShortBreak && r.Matches(facts));

    /// <summary>
    /// The four automatic fixes of punches, off to begin with (they change what payroll sees, so a tenant chooses them): a lone check-out or check-in near the
    /// start or end of the shift is read as the check-in or check-out, and of two punches close together at the start or end the first counts and the second is left out.
    /// </summary>
    private static IEnumerable<RuleSnapshot> AutoFixes(int priority)
    {
        yield return AutoFix("Read a lone check-out near the start as the check-in", priority, RuleAction.LoneCheckOutIsCheckIn, DefaultWindowMinutes);
        yield return AutoFix("Read a lone check-in near the end as the check-out", priority + 1, RuleAction.LoneCheckInIsCheckOut, DefaultWindowMinutes);
        yield return AutoFix("Keep the first of two close punches at the start", priority + 2, RuleAction.KeepFirstMorningPunch, DefaultWindowMinutes, DefaultGapMinutes);
        yield return AutoFix("Keep the first of two close punches at the end", priority + 3, RuleAction.KeepFirstEveningPunch, DefaultWindowMinutes, DefaultGapMinutes);
    }

    /// <summary>
    /// The two break fixes, off to begin with: a break-in near the scheduled start of the break, where no break is open, is read as the break-out; a break-out
    /// near the scheduled end of the break, where a break is already open, is read as the break-in.
    /// </summary>
    private static IEnumerable<RuleSnapshot> BreakFixes(int priority)
    {
        yield return AutoFix("Read a break-in near the break start as the break-out", priority, RuleAction.BreakInIsBreakOut, DefaultBreakWindowMinutes);
        yield return AutoFix("Read a break-out near the break end as the break-in", priority + 1, RuleAction.BreakOutIsBreakIn, DefaultBreakWindowMinutes);
    }

    /// <summary>The rule that keeps the last of two close check-outs at the end of the day, off to begin with: of the two, the second is the check-out and the first is left out.</summary>
    private static RuleSnapshot KeepLast(int priority) =>
        AutoFix("Keep the last of two close check-outs at the end", priority, RuleAction.KeepLastEveningPunch, DefaultWindowMinutes, DefaultGapMinutes);

    /// <summary>The check-in then check-out rule at the end, and the keep-the-last rule at the start, both off to begin with.</summary>
    private static IEnumerable<RuleSnapshot> PunchPairs(int priority)
    {
        yield return AutoFix("Read a check-in then a check-out at the end as one check-out", priority, RuleAction.KeepFirstEveningCheckInPunch, DefaultWindowMinutes, DefaultGapMinutes);
        yield return AutoFix("Keep the last of two close check-ins at the start", priority + 1, RuleAction.KeepLastMorningPunch, DefaultWindowMinutes, DefaultGapMinutes);
    }

    /// <summary>The rule that keeps the check-out of a check-in then a check-out at the end, and the one that keeps the check-in of a check-out then a check-in at the start, both off to begin with.</summary>
    private static IEnumerable<RuleSnapshot> CheckPairs(int priority)
    {
        yield return AutoFix("Keep the check-out of a check-in then a check-out at the end", priority, RuleAction.KeepEveningCheckOutPunch, DefaultWindowMinutes, DefaultGapMinutes);
        yield return AutoFix("Keep the check-in of a check-out then a check-in at the start", priority + 1, RuleAction.KeepMorningCheckInPunch, DefaultWindowMinutes, DefaultGapMinutes);
    }

    /// <summary>
    /// The five rules for repeated punches, off to begin with. They look at how far apart two punches are and nothing else, so they need no shift: they are
    /// how staff paid by the hour get a double tap read, and how a tap in the middle of a day is.
    /// </summary>
    private static IEnumerable<RuleSnapshot> Repeats(int priority)
    {
        yield return Repeat("Keep the first of two close check-ins", priority, RuleAction.KeepFirstRepeatedCheckIn);
        yield return Repeat("Keep the last of two close check-ins", priority + 1, RuleAction.KeepLastRepeatedCheckIn);
        yield return Repeat("Keep the first of two close check-outs", priority + 2, RuleAction.KeepFirstRepeatedCheckOut);
        yield return Repeat("Keep the last of two close check-outs", priority + 3, RuleAction.KeepLastRepeatedCheckOut);
        yield return Repeat("Keep the first of two close break punches of the same kind", priority + 4, RuleAction.KeepFirstRepeatedBreakPunch);
    }

    private static RuleSnapshot Repeat(string name, int priority, RuleAction action) =>
        new(name, priority, false, RuleStage.Interpretation, action, null, null, [new ConditionSnapshot(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual, DefaultGapMinutes)]);

    private static RuleSnapshot AutoFix(string name, int priority, RuleAction action, int windowMinutes, int? gapMinutes = null)
    {
        var conditions = new List<ConditionSnapshot> { new(RuleFact.PunchOffsetMinutes, RuleOperator.LessOrEqual, windowMinutes) };
        if (gapMinutes is { } gap)
        {
            conditions.Add(new ConditionSnapshot(RuleFact.PunchGapMinutes, RuleOperator.LessOrEqual, gap));
        }

        return new RuleSnapshot(name, priority, false, RuleStage.Interpretation, action, null, null, conditions);
    }

    /// <summary>The rule for a break shorter than the policy's minimum: on when the policy has one (0 turns it off, as it did).</summary>
    private static RuleSnapshot ShortBreak(int priority, AttendancePolicy policy) => new(
        "Count a short break as work",
        priority,
        policy.IgnoreBreaksShorterThanMinutes > 0,
        RuleStage.Interpretation,
        RuleAction.IgnoreShortBreak,
        null,
        null,
        [new ConditionSnapshot(RuleFact.BreakMinutes, RuleOperator.LessThan, policy.IgnoreBreaksShorterThanMinutes > 0 ? policy.IgnoreBreaksShorterThanMinutes : 10)]);

    private static RuleSnapshot Raise(string name, int priority, RuleFinding finding, bool enabled, AttendancePolicy policy) =>
        new(name, priority, enabled, RuleStage.Exception, RuleAction.RaiseException, finding, null, Threshold(finding, policy) is { } threshold ? [threshold] : []);

    /// <summary>The condition that says when the policy's numbers make a finding of this kind: past the break allowance, past the start allowance, or the wait after the shift.</summary>
    private static ConditionSnapshot? Threshold(RuleFinding? finding, AttendancePolicy policy) => finding switch
    {
        RuleFinding.LongBreak => new ConditionSnapshot(RuleFact.MinutesOver, RuleOperator.GreaterThan, policy.LongBreakGraceMinutes),
        RuleFinding.LateBreak or RuleFinding.EarlyBreak => new ConditionSnapshot(RuleFact.MinutesOver, RuleOperator.GreaterThan, policy.BreakStartGraceMinutes),
        RuleFinding.NoShow or RuleFinding.MissingCheckOut => new ConditionSnapshot(RuleFact.MinutesAfterShiftEnd, RuleOperator.GreaterOrEqual, policy.MissingPunchGraceMinutes),

        // How much less than scheduled is a problem starts as what the policy already forgives at the start and at the end of the shift.
        RuleFinding.ShortHours => new ConditionSnapshot(RuleFact.MinutesOver, RuleOperator.GreaterThan, policy.LateGraceMinutes + policy.EarlyDepartureGraceMinutes),

        // How late or early beyond the policy's grace (already taken off the measure) is a problem: any, to begin with.
        RuleFinding.LateCheckIn or RuleFinding.EarlyCheckOut => new ConditionSnapshot(RuleFact.MinutesOver, RuleOperator.GreaterThan, 0),

        // Overtime is already held to the policy's threshold when it is measured; the rule allows more on top, none to begin with.
        RuleFinding.Overtime => new ConditionSnapshot(RuleFact.MinutesOver, RuleOperator.GreaterThan, 0),
        _ => null,
    };
}
