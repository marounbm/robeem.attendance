namespace robeem.Contracts.Attendance;

/// <summary>
/// One rule, as shown and as sent back when a draft is saved. The rules of a set are looked at in the order they are listed.
/// <see cref="Sentence"/> is made by the server and ignored on input.
/// </summary>
public sealed class RuleDto
{
    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the stage: <c>Interpretation</c> or <c>Exception</c>.</summary>
    public string Stage { get; set; } = "Exception";

    /// <summary>Gets or sets the action: <c>InferBreak</c>, <c>RaiseException</c> or <c>SuppressException</c>.</summary>
    public string Action { get; set; } = "RaiseException";

    /// <summary>Gets or sets the finding a raise or suppress rule is about: <c>LongBreak</c>, <c>LateBreak</c>, <c>EarlyBreak</c>, <c>NoBreak</c> or <c>NoShow</c>.</summary>
    public string? Finding { get; set; }

    /// <summary>Gets or sets why a finding is suppressed; required for <c>SuppressException</c>.</summary>
    public string? Reason { get; set; }

    public List<ConditionDto> Conditions { get; set; } = [];

    /// <summary>Gets or sets the rule as a sentence a person can read.</summary>
    public string Sentence { get; set; } = string.Empty;
}
