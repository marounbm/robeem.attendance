namespace robeem.Contracts.Attendance;

/// <summary>A set of rules for one policy scope: the one in force, a draft being edited, or an earlier version.</summary>
public sealed class RuleSetDto
{
    /// <summary>Gets or sets the stored set's id; null for the rules that come from the policy (nothing is published yet).</summary>
    public int? Id { get; set; }

    /// <summary>Gets or sets the schedule the rules are for; null for the tenant-wide ones.</summary>
    public int? TimeScheduleId { get; set; }

    public string ScopeTitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of the publication; 0 for a draft or for the rules that come from the policy.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets <c>Draft</c>, <c>Published</c> or <c>Retired</c>; <c>Policy</c> for the rules that come from the policy.</summary>
    public string Status { get; set; } = "Policy";

    public DateTime? PublishedAt { get; set; }

    public string? Notes { get; set; }

    public List<RuleDto> Rules { get; set; } = [];
}
