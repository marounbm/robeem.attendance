using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>Editing the draft rules of one scope. The rules are posted back whole, in the order they are looked at.</summary>
public sealed class RulesEditViewModel
{
    /// <summary>Gets the actions a rule can take, with the words the form shows.</summary>
    public static IReadOnlyList<(string Value, string Label)> Actions { get; } =
    [
        ("RaiseException", "Raise an exception"),
        ("SuppressException", "Do not raise an exception"),
        ("InferBreak", "Read the gap as the break"),
        ("IgnoreShortBreak", "Count a short break as work"),
    ];

    /// <summary>Gets what a raise or suppress rule can be about.</summary>
    public static IReadOnlyList<(string Value, string Label)> Findings { get; } =
    [
        ("LongBreak", "a long break"),
        ("LateBreak", "a late break"),
        ("EarlyBreak", "an early break"),
        ("NoBreak", "a shift worked with no break"),
        ("NoShow", "a no-show"),
    ];

    /// <summary>Gets the facts a condition can look at, with the words the form shows and the stage each belongs to.</summary>
    public static IReadOnlyList<(string Value, string Label)> Facts { get; } =
    [
        ("MinutesOver", "Minutes over what is allowed (exception rules)"),
        ("ExcusedMinutes", "Minutes of the shift on approved leave (exception rules)"),
        ("MinutesAfterShiftEnd", "Minutes since the shift ended (no-show rules only)"),
        ("BreakMinutes", "Length of the break, minutes (counting a short break as work)"),
        ("GapMinutes", "Length of the gap, minutes (reading a break)"),
        ("GapStartOffsetMinutes", "Gap start from the scheduled break, minutes (reading a break)"),
        ("GapExcusedPercent", "Share of the gap on approved leave, % (reading a break)"),
        ("ShiftHasBreak", "The shift has a break (1 yes, 0 no)"),
        ("ShiftIsOvernight", "The shift ends after midnight (1 yes, 0 no)"),
    ];

    public static IReadOnlyList<(string Value, string Label)> Operators { get; } =
    [
        ("Equals", "is"),
        ("NotEquals", "is not"),
        ("LessThan", "is less than"),
        ("LessOrEqual", "is at most"),
        ("GreaterThan", "is more than"),
        ("GreaterOrEqual", "is at least"),
    ];

    [BindNever]
    public int DraftId { get; set; }

    [BindNever]
    public int? ScheduleId { get; set; }

    /// <summary>Gets or sets the place of the rule being changed on its own (<c>EditRule</c>), counting from 0.</summary>
    [BindNever]
    public int Position { get; set; }

    /// <summary>Gets or sets the version of the rules in force that the single rule was read from.</summary>
    [BindNever]
    public int ExpectedVersion { get; set; }

    [BindNever]
    public string ScopeTitle { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public List<RuleDto> Rules { get; set; } = [];
}
