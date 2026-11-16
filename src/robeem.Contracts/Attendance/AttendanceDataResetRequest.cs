namespace robeem.Contracts.Attendance;

/// <summary>Deletes all of the current tenant's attendance data. The tenant id is typed to confirm, so a click alone can't do it.</summary>
public sealed class AttendanceDataResetRequest
{
    public string Confirmation { get; set; } = string.Empty;
}
