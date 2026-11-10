using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// The rules in force for one policy scope, and the questions the calculation asks them. Pure: no I/O. With no published rule set the
/// rules are <see cref="FromPolicy"/>: the policy's own settings written as rules, so nothing changes until a set is published.
/// </summary>
public sealed class RuleSetSnapshot
{
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
    /// finding the policy raises. The thresholds of the policy that decide whether there is a finding at all stay in the policy.
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
            Raise("Raise an exception for a long break", 20, RuleFinding.LongBreak, policy.RaiseExceptionOnLongBreak),
            Raise("Raise an exception for a late break", 30, RuleFinding.LateBreak, policy.RaiseExceptionOnLateBreak),
            Raise("Raise an exception for an early break", 40, RuleFinding.EarlyBreak, policy.RaiseExceptionOnEarlyBreak),
            Raise("Raise an exception when no break was taken", 50, RuleFinding.NoBreak, policy.RaiseExceptionOnNoBreak),
            Raise("Raise an exception for a no-show", 60, RuleFinding.NoShow, true),
        };

        return new RuleSetSnapshot(0, "the policy", rules);
    }

    /// <summary>
    /// Whether the finding becomes an exception. The rules for the finding are looked at in priority order and the first whose
    /// conditions hold decides: a raise rule raises, a suppress rule does not. With none, nothing is raised.
    /// </summary>
    public bool Raises(RuleFinding finding, RuleFacts facts)
    {
        var decided = Rules.FirstOrDefault(r => r.Stage == RuleStage.Exception && r.Finding == finding && r.Matches(facts));
        return decided is { Action: RuleAction.RaiseException };
    }

    /// <summary>Whether the gap described by <paramref name="gap"/> is read as the break: the first enabled interpretation rule whose conditions hold decides.</summary>
    public bool InfersBreak(RuleFacts gap) =>
        Rules.Any(r => r.Stage == RuleStage.Interpretation && r.Action == RuleAction.InferBreak && r.Matches(gap));

    private static RuleSnapshot Raise(string name, int priority, RuleFinding finding, bool enabled) =>
        new(name, priority, enabled, RuleStage.Exception, RuleAction.RaiseException, finding, null, []);
}
