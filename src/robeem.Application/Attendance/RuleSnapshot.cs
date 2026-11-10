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
    public bool Matches(RuleFacts facts) => IsEnabled && Conditions.All(c => c.Holds(facts));
}
