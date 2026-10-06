using robeem.Contracts.HR.Schedules;

namespace robeem.Web.Models;

public sealed class TimeScheduleDetailsViewModel
{
    public required TimeScheduleDetail Schedule { get; init; }

    public int CyclePeriod => Schedule.Settings.CyclePeriod;

    /// <summary>Gets a value indicating whether the pattern is one week, whose days are weekdays.</summary>
    public bool IsWeekly => CyclePeriod == 7;

    /// <summary>
    /// Names a day of the pattern: the weekday for a weekly pattern (day 1 is the weekday the week
    /// starts on), otherwise "Day n".
    /// </summary>
    public string DayLabel(int cycleDay)
    {
        if (!IsWeekly || !Enum.TryParse<DayOfWeek>(Schedule.Settings.FirstCycleDay, out var first))
        {
            return $"Day {cycleDay}";
        }

        return ((DayOfWeek)(((int)first + cycleDay - 1) % 7)).ToString();
    }

    /// <summary>Gets the employees of a team, for the members list.</summary>
    public IEnumerable<ScheduleEmployeeItem> MembersOf(ScheduleTeamItem team) => Schedule.Employees.Where(e => e.TeamId == team.Id);
}
