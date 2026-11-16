namespace robeem.Contracts.Attendance;

/// <summary>Settings to save. They change the standard rules of the scope and publish them at once as the next version.</summary>
public sealed class RuleSettingsSaveRequest
{
    public int? TimeScheduleId { get; set; }

    /// <summary>Gets or sets the version of the rules the person was looking at; the save is refused when the rules in force are another.</summary>
    public int ExpectedVersion { get; set; }

    public RuleSettings Settings { get; set; } = new();
}
