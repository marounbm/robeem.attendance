using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Writes a rule as a sentence a person can read, so what the system will do is plain without reading the conditions.</summary>
public static class RuleSentence
{
    public static string Describe(RuleDto rule)
    {
        var conditions = rule.Conditions.Select(Condition).ToList();
        var action = Action(rule);
        return conditions.Count == 0
            ? $"Always {action}."
            : $"When {string.Join(" and ", conditions)}, {action}.";
    }

    private static string Action(RuleDto rule)
    {
        var finding = Enum.TryParse<RuleFinding>(rule.Finding, out var f) ? FindingText(f) : "a finding";
        return Enum.TryParse<RuleAction>(rule.Action, out var action)
            ? action switch
            {
                RuleAction.InferBreak => "read the gap as the break (marked as inferred)",
                RuleAction.RaiseException => $"raise an exception for {finding}",
                RuleAction.SuppressException => $"do not raise an exception for {finding}" + (string.IsNullOrWhiteSpace(rule.Reason) ? string.Empty : $" (reason: {rule.Reason.Trim()})"),
                _ => "do nothing",
            }
            : "do nothing";
    }

    private static string FindingText(RuleFinding finding) => finding switch
    {
        RuleFinding.LongBreak => "a long break",
        RuleFinding.LateBreak => "a late break",
        RuleFinding.EarlyBreak => "an early break",
        RuleFinding.NoBreak => "a shift worked with no break",
        RuleFinding.NoShow => "a no-show",
        _ => "a finding",
    };

    private static string Condition(ConditionDto condition)
    {
        if (!Enum.TryParse<RuleFact>(condition.Fact, out var fact) || !Enum.TryParse<RuleOperator>(condition.Operator, out var op))
        {
            return "an unknown condition holds";
        }

        var value = condition.Value;
        var words = op switch
        {
            RuleOperator.Equals => "exactly",
            RuleOperator.NotEquals => "not",
            RuleOperator.LessThan => "less than",
            RuleOperator.LessOrEqual => "at most",
            RuleOperator.GreaterThan => "more than",
            RuleOperator.GreaterOrEqual => "at least",
            _ => string.Empty,
        };

        // A yes-or-no fact reads as a statement.
        var yes = (op == RuleOperator.Equals) == (value == 1);
        return fact switch
        {
            RuleFact.GapMinutes => $"the gap lasts {words} {value} minutes",
            RuleFact.GapStartOffsetMinutes => $"the gap starts {words} {value} minutes from the scheduled break",
            RuleFact.GapExcusedPercent => $"approved leave covers {words} {value}% of the gap",
            RuleFact.MinutesOver => $"the finding is {words} {value} minutes over what is allowed",
            RuleFact.ExcusedMinutes => $"approved leave covers {words} {value} minutes of the shift",
            RuleFact.ShiftHasBreak => yes ? "the shift has a break" : "the shift has no break",
            RuleFact.ShiftIsOvernight => yes ? "the shift ends after midnight" : "the shift ends the same day",
            _ => "a condition holds",
        };
    }
}
