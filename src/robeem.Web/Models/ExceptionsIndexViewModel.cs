using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class ExceptionsIndexViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Active", "Open", "PendingCorrection", "Closed", "All"];

    public IReadOnlyList<ExceptionListItem> Items { get; init; } = Array.Empty<ExceptionListItem>();

    public string Status { get; init; } = "Active";

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Gets the exceptions grouped by the day they are on, newest day first: the rows of the grid.</summary>
    public IReadOnlyList<ExceptionDay> Days =>
        Items
            .GroupBy(i => (i.EmployeeId, i.AttendanceDate.Date))
            .Select(g => new ExceptionDay(g.Key.EmployeeId, g.First().EmployeeName, g.Key.Date, g.OrderBy(i => i.Id).ToList()))
            .OrderByDescending(d => d.Date)
            .ThenBy(d => d.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
