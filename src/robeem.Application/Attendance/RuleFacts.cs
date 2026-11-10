using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What a rule's conditions can look at, for one finding or one gap. Only the facts that belong to the rule's stage are set; the others
/// are 0. The facts are whole numbers: minutes, percent, or 1 and 0 for a yes-or-no fact.
/// </summary>
public sealed record RuleFacts
{
    public int MinutesOver { get; init; }

    public bool ShiftHasBreak { get; init; }

    public bool ShiftIsOvernight { get; init; }

    public int ExcusedMinutes { get; init; }

    public int GapMinutes { get; init; }

    public int GapStartOffsetMinutes { get; init; }

    public int GapExcusedPercent { get; init; }

    public int ValueOf(RuleFact fact) => fact switch
    {
        RuleFact.GapMinutes => GapMinutes,
        RuleFact.GapStartOffsetMinutes => GapStartOffsetMinutes,
        RuleFact.GapExcusedPercent => GapExcusedPercent,
        RuleFact.MinutesOver => MinutesOver,
        RuleFact.ShiftHasBreak => ShiftHasBreak ? 1 : 0,
        RuleFact.ShiftIsOvernight => ShiftIsOvernight ? 1 : 0,
        RuleFact.ExcusedMinutes => ExcusedMinutes,
        _ => 0,
    };
}
