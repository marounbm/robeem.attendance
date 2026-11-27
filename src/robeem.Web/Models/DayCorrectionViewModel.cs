using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class DayCorrectionViewModel
{
    /// <summary>Gets the types a punch can have or be given.</summary>
    public static IReadOnlyList<string> EventTypes { get; } = ["CheckIn", "BreakOut", "BreakIn", "CheckOut"];

    public required DayCorrectionView Day { get; init; }

    public required DayCorrectionForm Form { get; init; }

    /// <summary>
    /// Gets the Attendance list the day was opened from, when it was, so that the day's page can offer the next day of that list again once the
    /// day has been corrected. Null when the page was opened some other way.
    /// </summary>
    public RecordListContext? List { get; init; }

    /// <summary>Gets the route values of the day's page: the day, and the list it came from when there is one.</summary>
    public Dictionary<string, string?> BackToDayValues()
    {
        var values = List is { Position: > 0 } ? List.ToDayValues(List.Position) : new Dictionary<string, string?>();
        values["employeeId"] = Day.EmployeeId.ToString(CultureInfo.InvariantCulture);
        values["date"] = Day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return values;
    }
}
