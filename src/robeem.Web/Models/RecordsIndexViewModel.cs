using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class RecordsIndexViewModel
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<string> Statuses { get; } = ["Any", "Calculated", "PendingReview", "Approved", "Rejected"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the days on the page.</summary>
    public IReadOnlyList<RecordDayListItem> Items { get; init; } = Array.Empty<RecordDayListItem>();

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of days that match the filters, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets the status shown, or <c>Any</c>.</summary>
    public string Status { get; init; } = "Any";

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public bool OnlyWithExceptions { get; init; }

    /// <summary>Gets the one employee shown, or null for everyone the user may see.</summary>
    public int? EmployeeId { get; init; }

    /// <summary>Gets the employees the user can narrow the list to.</summary>
    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();

    /// <summary>Gets what the pager needs: the page, and the filters that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    private Dictionary<string, string?> FilterValues()
    {
        var values = new Dictionary<string, string?> { ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture) };
        if (Status != "Any")
        {
            values["status"] = Status;
        }

        if (From is { } from)
        {
            values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (OnlyWithExceptions)
        {
            values["onlyWithExceptions"] = "true";
        }

        if (EmployeeId is { } employeeId)
        {
            values["employeeId"] = employeeId.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }
}
