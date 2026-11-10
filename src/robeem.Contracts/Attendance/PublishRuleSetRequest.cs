namespace robeem.Contracts.Attendance;

/// <summary>What is sent to publish a draft: an optional note for the history.</summary>
public sealed class PublishRuleSetRequest
{
    public string? Notes { get; set; }
}
