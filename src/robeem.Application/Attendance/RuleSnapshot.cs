using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>One rule, as the evaluator reads it. All its conditions must hold.</summary>
public sealed record RuleSnapshot(
    string Name,
    int Priority,
    bool IsEnabled,
    RuleStage Stage,
    RuleAction Action,
    RuleFinding? Finding,
    string? Reason,
    IReadOnlyList<ConditionSnapshot> Conditions)
{
    public bool Matches(RuleFacts facts) => IsEnabled && ConditionsHold(facts);

    /// <summary>Whether every condition holds, whether the rule is on or off. A rule that is off still says what is worth noting on the day.</summary>
    public bool ConditionsHold(RuleFacts facts) => Conditions.All(c => c.Holds(facts));
}
