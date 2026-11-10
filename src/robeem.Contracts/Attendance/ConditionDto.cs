namespace robeem.Contracts.Attendance;

/// <summary>One condition of a rule: a fact, how it is compared, and the number it is compared with. Names are those of the rule enums.</summary>
public sealed class ConditionDto
{
    public string Fact { get; set; } = string.Empty;

    public string Operator { get; set; } = string.Empty;

    public int Value { get; set; }
}
