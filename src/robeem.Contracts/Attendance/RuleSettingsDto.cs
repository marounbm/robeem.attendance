namespace robeem.Contracts.Attendance;

/// <summary>The rules in force for a scope read as plain settings, or the reason they can't be.</summary>
public sealed class RuleSettingsDto
{
    public int? TimeScheduleId { get; set; }

    public string ScopeTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of the rules in force that these settings were read from (0: the policy's starting rules).</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets a value indicating whether the rules are the standard ones, so that they can be shown and saved as settings.</summary>
    public bool IsStandard { get; set; }

    /// <summary>Gets or sets why the rules are not standard (they were customised, or a draft is open); null when they are.</summary>
    public string? NotStandardReason { get; set; }

    /// <summary>Gets or sets the settings; meaningful only when <see cref="IsStandard"/>.</summary>
    public RuleSettings Settings { get; set; } = new();
}
