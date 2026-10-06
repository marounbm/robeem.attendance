using robeem.Contracts.HR.Schedules;

namespace robeem.Web.Models;

public sealed class TimeScheduleFormViewModel
{
    /// <summary>Gets the weekdays a week can start on, Monday first.</summary>
    public static IReadOnlyList<string> Weekdays { get; } =
        [nameof(DayOfWeek.Monday), nameof(DayOfWeek.Tuesday), nameof(DayOfWeek.Wednesday), nameof(DayOfWeek.Thursday), nameof(DayOfWeek.Friday), nameof(DayOfWeek.Saturday), nameof(DayOfWeek.Sunday)];

    public TimeScheduleInput Input { get; set; } = new();
}
