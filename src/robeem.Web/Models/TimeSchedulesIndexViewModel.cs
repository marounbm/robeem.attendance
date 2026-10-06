using robeem.Contracts.HR.Schedules;

namespace robeem.Web.Models;

public sealed class TimeSchedulesIndexViewModel
{
    public IReadOnlyList<TimeScheduleListItem> Schedules { get; init; } = Array.Empty<TimeScheduleListItem>();
}
