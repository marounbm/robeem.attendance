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
            Raise("Raise an exception for a no-show", 60, RuleFinding.NoShow, true, policy),
            Raise("Raise an exception for a missing check-out", 65, RuleFinding.MissingCheckOut, true, policy),
            ShortBreak(70, policy),
        };

        return new RuleSetSnapshot(0, "the policy", rules);
    }

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
        _ => null,
    };
}
