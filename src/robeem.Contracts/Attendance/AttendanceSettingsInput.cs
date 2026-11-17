namespace robeem.Contracts.Attendance;

public sealed class AttendanceSettingsInput
{
    /// <summary>Gets or sets the date the punching started; empty clears it. It can't be in the future.</summary>
    public DateTime? StartDate { get; set; }
}
