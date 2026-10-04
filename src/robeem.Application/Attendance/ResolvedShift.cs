namespace robeem.Application.Attendance;

/// <summary>
/// One shift an employee is expected to work on a given date: the study's
/// <c>EmployeeSchedule</c>/<c>WorkShift</c> (CLAUDE.md §3.1). Times are <c>"HH:mm"</c> strings,
/// as on <c>TimeShiftEntity</c>.
/// </summary>
public sealed record ResolvedShift(
    int TimeScheduleShiftId,
    string StartTime,
    string EndTime,
    bool BreakTime,
    string BreakStartTime,
    string BreakEndTime,
    bool Overtime,
    bool IsOvernight)
{
    /// <summary>Gets when the shift starts: the resolved date at <see cref="StartTime"/>, local wall-clock time.</summary>
    public DateTime StartsAt { get; init; }

    /// <summary>Gets when the shift ends; the next day for an overnight shift.</summary>
    public DateTime EndsAt { get; init; }
}
