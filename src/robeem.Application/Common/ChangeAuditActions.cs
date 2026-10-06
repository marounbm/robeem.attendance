namespace robeem.Application.Common;

/// <summary>The actions services outside Attendance record through <see cref="IChangeAuditTrail"/>.</summary>
public static class ChangeAuditActions
{
    /// <summary>A time schedule, one of its shifts, teams or rosters, or who is in a team was created, changed or removed.</summary>
    public const string ScheduleChanged = "ScheduleChanged";
}
