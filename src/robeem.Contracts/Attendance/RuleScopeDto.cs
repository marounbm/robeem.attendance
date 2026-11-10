namespace robeem.Contracts.Attendance;

/// <summary>A scope rules can be set for: the whole tenant, or a time schedule that has a policy of its own.</summary>
public sealed class RuleScopeDto
{
    public int? TimeScheduleId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a rule set is published for the scope.</summary>
    public bool HasPublished { get; set; }

    /// <summary>Gets or sets a value indicating whether a draft is waiting for the scope.</summary>
    public bool HasDraft { get; set; }
}
