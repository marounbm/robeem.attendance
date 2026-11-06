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

    /// <summary>Gets the column the list is sorted by; without a choice, the date.</summary>
    public RecordSortField Sort { get; init; } = RecordSortField.Date;

    /// <summary>Gets a value indicating whether the sort runs from the highest to the lowest (for the date, newest first).</summary>
    public bool Descending { get; init; }

    /// <summary>Gets what the pager needs: the page, and the filters and sort that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    /// <summary>Gets the name a column has in the address.</summary>
    public static string SortName(RecordSortField field) => field.ToString().ToLowerInvariant();

    /// <summary>
    /// Gets the route values of a column heading's link: the filters and page size as they are, no page (sorting starts again at the first),
    /// and the column. Choosing the column the list is already sorted by, ascending, turns it round; any other starts ascending.
    /// </summary>
    public Dictionary<string, string?> SortLink(RecordSortField field)
    {
        var values = Filters();
        values["sort"] = SortName(field);
        values["dir"] = Sort == field && !Descending ? "desc" : "asc";
        return values;
    }

    /// <summary>Gets the sort mark for a column heading, for <c>aria-sort</c>: <c>ascending</c>, <c>descending</c> or <c>none</c>.</summary>
    public string AriaSort(RecordSortField field) => Sort != field ? "none" : Descending ? "descending" : "ascending";

    private Dictionary<string, string?> FilterValues()
    {
        var values = Filters();
        if (Sort != RecordSortField.Date || Descending)
        {
            values["sort"] = SortName(Sort);
            values["dir"] = Descending ? "desc" : "asc";
        }

        return values;
    }

    private Dictionary<string, string?> Filters()
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
