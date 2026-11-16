using Microsoft.AspNetCore.Mvc.ModelBinding;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The rules of one scope as plain settings, to read and to change. Only <see cref="Settings"/> comes back from the form.</summary>
public sealed class RulesSettingsViewModel
{
    [BindNever]
    public IReadOnlyList<RuleScopeDto> Scopes { get; init; } = Array.Empty<RuleScopeDto>();

    [BindNever]
    public int? ScheduleId { get; init; }

    [BindNever]
    public string ScopeTitle { get; init; } = string.Empty;

    /// <summary>Gets the version of the rules in force that the settings were read from (0: the policy's starting rules).</summary>
    [BindNever]
    public int Version { get; init; }

    /// <summary>Gets a value indicating whether the rules are the standard ones, so they can be changed as settings.</summary>
    [BindNever]
    public bool IsStandard { get; init; }

    [BindNever]
    public string? NotStandardReason { get; init; }

    /// <summary>Gets a value indicating whether the scope has a draft open, which stops the settings being saved.</summary>
    [BindNever]
    public bool HasDraft { get; init; }

    public RuleSettings Settings { get; set; } = new();
}
