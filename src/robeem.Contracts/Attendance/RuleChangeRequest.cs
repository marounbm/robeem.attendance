namespace robeem.Contracts.Attendance;

/// <summary>
/// A change to one rule of a scope, made from the rules grid. A published set is never edited, so the change is published at once as the next
/// version, with every other rule as it was.
/// </summary>
public sealed class RuleChangeRequest
{
    /// <summary>Gets or sets the schedule whose rules are changed; none is the whole tenant.</summary>
    public int? TimeScheduleId { get; set; }

    /// <summary>Gets or sets the version of the rules the person was looking at (0: the policy's starting rules). A change is refused when the rules in force are another version.</summary>
    public int ExpectedVersion { get; set; }

    /// <summary>Gets or sets what to do, one of <see cref="RuleChangeOperations"/>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>Gets or sets the rule's place in the list, counting from 0.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the rule that takes the place of the one at <see cref="Position"/>. Used by <see cref="RuleChangeOperations.Replace"/> only.</summary>
    public RuleDto? Rule { get; set; }

    /// <summary>Gets or sets a note for the history; when empty, the history says what was changed.</summary>
    public string? Notes { get; set; }
}
