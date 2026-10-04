using robeem.DataModel;

namespace robeem.Application.Attendance;

public static class TimeShiftEntityExtensions
{
    /// <summary>
    /// Whether the shift ends on the day after it starts, e.g. 22:00-06:00. Derived, never stored
    /// (CLAUDE.md §2.6, §3). <see cref="Set.TimeSpan(string, string)"/> already counts such a shift
    /// into the next day.
    /// </summary>
    public static bool IsOvernight(this TimeShiftEntity shift) =>
        Set.ParseTime(shift.EndTime) < Set.ParseTime(shift.StartTime);
}
