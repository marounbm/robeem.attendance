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

    /// <summary>Whether the action is one of the six that fix a day's punches.</summary>
    public static bool IsAutoFix(RuleAction action) => IsCheckInOutFix(action) || IsBreakFix(action);

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
            Raise("Raise an exception for a no-show", 60, RuleFinding.NoShow, true, policy),
            Raise("Raise an exception for a missing check-out", 65, RuleFinding.MissingCheckOut, true, policy),
            ShortBreak(70, policy),
        };
        rules.AddRange(AutoFixes(80));
        rules.AddRange(BreakFixes(84));

        return new RuleSetSnapshot(0, "the policy", rules);
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
        _ => null,
    };
}
