namespace robeem.Contracts.Attendance;

/// <summary>What is sent to save a draft: the whole list of its rules, in the order they are looked at.</summary>
public sealed class RulesSaveRequest
{
    public List<RuleDto> Rules { get; set; } = [];

    public string? Notes { get; set; }
}
