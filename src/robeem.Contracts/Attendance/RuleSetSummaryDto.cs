namespace robeem.Contracts.Attendance;

/// <summary>One line of the history of a scope's rule sets.</summary>
public sealed class RuleSetSummaryDto
{
    public int Id { get; set; }

    public int Version { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? PublishedAt { get; set; }

    public int? PublishedById { get; set; }

    public string? PublishedByName { get; set; }

    public int RuleCount { get; set; }

    public string? Notes { get; set; }
}
