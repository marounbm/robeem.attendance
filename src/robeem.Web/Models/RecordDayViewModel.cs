using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class RecordDayViewModel
{
    public required RecordDayDetail Day { get; init; }

    /// <summary>Gets a value indicating whether the acting user may run the day through the engine again (HR).</summary>
    public bool CanReprocess { get; init; }

    /// <summary>Gets the Attendance list the day was opened from, with the day's place in it; null when the day was opened any other way.</summary>
    public RecordListContext? List { get; init; }

    /// <summary>Gets the day that follows in that list, or null when this is the last one (or the day was not opened from a list).</summary>
    public RecordDayListItem? Next { get; init; }

    /// <summary>Gets the place of <see cref="Next"/> in the list.</summary>
    public int NextPosition { get; init; }

    /// <summary>Gets the route values of the link to the next day, carrying the list on.</summary>
    public Dictionary<string, string?> NextLink()
    {
        var values = List!.ToDayValues(NextPosition);
        values["employeeId"] = Next!.EmployeeId.ToString(CultureInfo.InvariantCulture);
        values["date"] = Next.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return values;
    }
}
