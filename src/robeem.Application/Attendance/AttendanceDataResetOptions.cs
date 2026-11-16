namespace robeem.Application.Attendance;

/// <summary>
/// Whether an administrator may delete all of a tenant's attendance data from the screen. Off unless a host turns it on (the Development
/// settings do): the data is gone for good, so a production host has to choose it.
/// </summary>
public sealed class AttendanceDataResetOptions
{
    public const string SectionName = "Attendance:DataReset";

    public bool Enabled { get; set; }
}
