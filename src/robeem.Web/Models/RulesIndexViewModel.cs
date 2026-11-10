using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The rules in force for one scope, who may change them, and the history of their versions.</summary>
public sealed class RulesIndexViewModel
{
    public IReadOnlyList<RuleScopeDto> Scopes { get; init; } = Array.Empty<RuleScopeDto>();

    /// <summary>Gets the schedule whose rules are shown; null is the whole tenant.</summary>
    public int? ScheduleId { get; init; }

    public RuleSetDto Current { get; init; } = new();

    public bool HasDraft { get; init; }

    public IReadOnlyList<RuleSetSummaryDto> History { get; init; } = Array.Empty<RuleSetSummaryDto>();
}
