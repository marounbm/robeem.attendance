using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Writes a rule as a sentence a person can read, so what the system will do is plain without reading the conditions.</summary>
public static class RuleSentence
{
    public static string Describe(RuleDto rule)
    {
        var conditions = rule.Conditions.Select(c => Condition(c, rule.Action)).ToList();
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
                RuleAction.IgnoreShortBreak => "count the break as work (its two punches are left out)",
                RuleAction.LoneCheckOutIsCheckIn => "read the check-out as the check-in (marked as inferred)",
                RuleAction.LoneCheckInIsCheckOut => "read the check-in as the check-out (marked as inferred)",
                RuleAction.KeepFirstMorningPunch => "count the first punch as the check-in and leave the second out",
                RuleAction.KeepFirstEveningPunch => "count the first punch as the check-out (marked as inferred when it was a check-in) and leave the second out",
                RuleAction.BreakInIsBreakOut => "read the break-in as the break-out (marked as inferred)",
                RuleAction.BreakOutIsBreakIn => "read the break-out as the break-in (marked as inferred)",
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
        RuleFinding.MissingCheckOut => "a missing check-out",
        RuleFinding.ShortHours => "less than the scheduled time worked",
        _ => "a finding",
    };

    private static string Condition(ConditionDto condition, string action)
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
            RuleFact.BreakMinutes => $"the break lasts {words} {value} minutes",
            RuleFact.MinutesAfterShiftEnd => $"the shift ended {words} {value} minutes ago",
            RuleFact.PunchOffsetMinutes when action == nameof(RuleAction.BreakInIsBreakOut) => $"the punch is {words} {value} minutes from the scheduled start of the break",
            RuleFact.PunchOffsetMinutes when action == nameof(RuleAction.BreakOutIsBreakIn) => $"the punch is {words} {value} minutes from the scheduled end of the break",
            RuleFact.PunchOffsetMinutes => $"the punch is {words} {value} minutes from the scheduled start or end of the shift",
            RuleFact.PunchGapMinutes => $"the two punches are {words} {value} minutes apart",
            RuleFact.ShiftHasBreak => yes ? "the shift has a break" : "the shift has no break",
            RuleFact.ShiftIsOvernight => yes ? "the shift ends after midnight" : "the shift ends the same day",
            _ => "a condition holds",
        };
    }
}
