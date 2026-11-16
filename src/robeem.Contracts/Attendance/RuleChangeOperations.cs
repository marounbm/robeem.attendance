namespace robeem.Contracts.Attendance;

/// <summary>What a <see cref="RuleChangeRequest"/> can do to one rule.</summary>
public static class RuleChangeOperations
{
    public const string Replace = "Replace";
    public const string Enable = "Enable";
    public const string Disable = "Disable";
    public const string Delete = "Delete";
    public const string MoveUp = "MoveUp";
    public const string MoveDown = "MoveDown";
}
