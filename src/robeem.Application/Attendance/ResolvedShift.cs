using robeem.DataModel;

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

    /// <summary>
    /// Gets when the scheduled break starts; null for a shift without a break. A break earlier in the day than the shift starts
    /// is after midnight, so it falls on the next day.
    /// </summary>
    public DateTime? BreakStartsAt
    {
        get
        {
            if (!BreakTime)
            {
                return null;
            }

            var at = StartsAt.Date.Add(Set.ParseTime(BreakStartTime));
            return at < StartsAt ? at.AddDays(1) : at;
        }
    }

    /// <summary>Gets the length of the scheduled break in minutes; 0 for a shift without a break.</summary>
    public int BreakMinutes => BreakTime ? (int)Math.Round(Set.TimeSpan(BreakStartTime, BreakEndTime) * 60) : 0;
}
