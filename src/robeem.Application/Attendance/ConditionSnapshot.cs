using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>One condition of a rule, as the evaluator reads it.</summary>
public sealed record ConditionSnapshot(RuleFact Fact, RuleOperator Operator, int Value)
{
    public bool Holds(RuleFacts facts)
    {
        var actual = facts.ValueOf(Fact);
        return Operator switch
        {
            RuleOperator.Equals => actual == Value,
            RuleOperator.NotEquals => actual != Value,
            RuleOperator.LessThan => actual < Value,
            RuleOperator.LessOrEqual => actual <= Value,
            RuleOperator.GreaterThan => actual > Value,
            RuleOperator.GreaterOrEqual => actual >= Value,
            _ => false,
        };
    }
}
