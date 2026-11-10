using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Checks a list of rules before they are saved or published: every name is one the system knows, the actions fit their stages, the
/// conditions use facts of their stage with sensible numbers and cannot contradict each other, and a suppression gives its reason.
/// Pure. Errors are keyed <c>Rules[i].Field</c>, so a form can show each by its field.
/// </summary>
public static class RuleValidator
{
    public const int MaxRules = 50;
    public const int MaxConditions = 10;

    private static readonly RuleFact[] InterpretationFacts =
        [RuleFact.GapMinutes, RuleFact.GapStartOffsetMinutes, RuleFact.GapExcusedPercent, RuleFact.ShiftHasBreak, RuleFact.ShiftIsOvernight];

    private static readonly RuleFact[] ExceptionFacts =
        [RuleFact.MinutesOver, RuleFact.ShiftHasBreak, RuleFact.ShiftIsOvernight, RuleFact.ExcusedMinutes];

    /// <summary>Gets the facts a condition of a rule of <paramref name="stage"/> may use.</summary>
    public static IReadOnlyList<RuleFact> FactsOf(RuleStage stage) => stage == RuleStage.Interpretation ? InterpretationFacts : ExceptionFacts;

    public static Dictionary<string, string> Validate(IReadOnlyList<RuleDto> rules)
    {
        var errors = new Dictionary<string, string>();
        if (rules.Count > MaxRules)
        {
            errors["Rules"] = $"A set can have at most {MaxRules} rules.";
            return errors;
        }

        for (var i = 0; i < rules.Count; i++)
        {
            ValidateRule(rules[i], $"Rules[{i}]", errors);
        }

        return errors;
    }

    private static void ValidateRule(RuleDto rule, string key, Dictionary<string, string> errors)
    {
        var name = rule.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 255)
        {
            errors[$"{key}.Name"] = "Give the rule a name of at most 255 characters.";
        }

        var stageOk = Enum.TryParse<RuleStage>(rule.Stage, out var stage) && Enum.IsDefined(stage);
        var actionOk = Enum.TryParse<RuleAction>(rule.Action, out var action) && Enum.IsDefined(action);
        if (!stageOk)
        {
            errors[$"{key}.Stage"] = $"The stage must be one of: {string.Join(", ", Enum.GetNames<RuleStage>())}.";
        }

        if (!actionOk)
        {
            errors[$"{key}.Action"] = $"The action must be one of: {string.Join(", ", Enum.GetNames<RuleAction>())}.";
        }

        if (stageOk && actionOk)
        {
            ValidateAction(rule, key, stage, action, errors);
        }

        if (rule.Conditions.Count > MaxConditions)
        {
            errors[$"{key}.Conditions"] = $"A rule can have at most {MaxConditions} conditions.";
            return;
        }

        if (!stageOk)
        {
            return;
        }

        var parsed = new List<(RuleFact Fact, RuleOperator Operator, int Value)>();
        for (var j = 0; j < rule.Conditions.Count; j++)
        {
            if (ValidateCondition(rule.Conditions[j], $"{key}.Conditions[{j}]", stage, errors) is { } condition)
            {
                parsed.Add(condition);
            }
        }

        foreach (var fact in parsed.Select(c => c.Fact).Distinct())
        {
            if (CanNeverHold(parsed.Where(c => c.Fact == fact)))
            {
                errors[$"{key}.Conditions"] = $"The conditions on \"{fact}\" contradict each other, so this rule can never apply.";
            }
        }
    }

    private static void ValidateAction(RuleDto rule, string key, RuleStage stage, RuleAction action, Dictionary<string, string> errors)
    {
        if (action == RuleAction.InferBreak)
        {
            if (stage != RuleStage.Interpretation)
            {
                errors[$"{key}.Stage"] = "Reading a break from a gap is an interpretation rule.";
            }

            if (!string.IsNullOrWhiteSpace(rule.Finding))
            {
                errors[$"{key}.Finding"] = "Reading a break is not about a finding.";
            }

            return;
        }

        if (stage != RuleStage.Exception)
        {
            errors[$"{key}.Stage"] = "Raising or suppressing an exception is an exception rule.";
        }

        if (!Enum.TryParse<RuleFinding>(rule.Finding, out var finding) || !Enum.IsDefined(finding))
        {
            errors[$"{key}.Finding"] = $"Choose what the rule is about: {string.Join(", ", Enum.GetNames<RuleFinding>())}.";
        }

        if (action == RuleAction.SuppressException)
        {
            var reason = rule.Reason?.Trim() ?? string.Empty;
            if (reason.Length == 0 || reason.Length > 500)
            {
                errors[$"{key}.Reason"] = "Give the reason a finding is not raised, in at most 500 characters. It is kept in the audit log.";
            }
        }
    }

    private static (RuleFact, RuleOperator, int)? ValidateCondition(ConditionDto condition, string key, RuleStage stage, Dictionary<string, string> errors)
    {
        if (!Enum.TryParse<RuleFact>(condition.Fact, out var fact) || !Enum.IsDefined(fact))
        {
            errors[$"{key}.Fact"] = $"The fact must be one of: {string.Join(", ", Enum.GetNames<RuleFact>())}.";
            return null;
        }

        if (!FactsOf(stage).Contains(fact))
        {
            errors[$"{key}.Fact"] = $"\"{fact}\" can't be used in a rule of the {stage} stage. Use: {string.Join(", ", FactsOf(stage))}.";
            return null;
        }

        if (!Enum.TryParse<RuleOperator>(condition.Operator, out var op) || !Enum.IsDefined(op))
        {
            errors[$"{key}.Operator"] = $"The operator must be one of: {string.Join(", ", Enum.GetNames<RuleOperator>())}.";
            return null;
        }

        var yesNo = fact is RuleFact.ShiftHasBreak or RuleFact.ShiftIsOvernight;
        if (yesNo && (condition.Value is not (0 or 1) || op is not (RuleOperator.Equals or RuleOperator.NotEquals)))
        {
            errors[$"{key}.Value"] = "A yes-or-no fact is compared with 1 (yes) or 0 (no) using \"equals\" or \"not equal\".";
            return null;
        }

        var max = fact == RuleFact.GapExcusedPercent ? 100 : 1440;
        if (!yesNo && (condition.Value < 0 || condition.Value > max))
        {
            errors[$"{key}.Value"] = $"Use a number between 0 and {max}.";
            return null;
        }

        return (fact, op, condition.Value);
    }

    private static bool CanNeverHold(IEnumerable<(RuleFact Fact, RuleOperator Operator, int Value)> conditions)
    {
        long low = int.MinValue, high = int.MaxValue;
        var excluded = new HashSet<int>();
        foreach (var (_, op, value) in conditions)
        {
            switch (op)
            {
                case RuleOperator.Equals:
                    (low, high) = (Math.Max(low, value), Math.Min(high, value));
                    break;
                case RuleOperator.NotEquals:
                    excluded.Add(value);
                    break;
                case RuleOperator.LessThan:
                    high = Math.Min(high, value - 1L);
                    break;
                case RuleOperator.LessOrEqual:
                    high = Math.Min(high, value);
                    break;
                case RuleOperator.GreaterThan:
                    low = Math.Max(low, value + 1L);
                    break;
                case RuleOperator.GreaterOrEqual:
                    low = Math.Max(low, value);
                    break;
            }
        }

        if (low > high)
        {
            return true;
        }

        // A single value left, and it is one the conditions rule out.
        return low == high && excluded.Contains((int)low);
    }
}
