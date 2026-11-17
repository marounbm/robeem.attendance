namespace robeem.Contracts.Attendance;

/// <summary>The attendance settings of the tenant, and the dates that frame them.</summary>
public sealed class AttendanceSettingsItem
{
    /// <summary>Gets the date the punching started; null when not set. No attendance is expected before it.</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>Gets the last day a day was calculated for, which is the last day attendance was entered; null when there is none.</summary>
    public DateTime? LastAttendanceDate { get; init; }
}
