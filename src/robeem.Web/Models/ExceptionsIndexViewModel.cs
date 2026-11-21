using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of the exception groups (an employee's shift on a date, with its exceptions), and the filter that chose it.</summary>
public sealed class ExceptionsIndexViewModel
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<string> Statuses { get; } = ["Active", "Open", "PendingCorrection", "Closed", "All"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the groups on the page: the rows of the grid.</summary>
    public IReadOnlyList<ExceptionGroupListItem> Groups { get; init; } = Array.Empty<ExceptionGroupListItem>();

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of groups that match the filters, on every page together.</summary>
    public int Total { get; init; }

    public string Status { get; init; } = "Active";

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

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

    /// <summary>Gets what an action sends back with it, so the list is shown again as it was: the filters, the page size and the page.</summary>
    public IReadOnlyDictionary<string, string?> ReturnValues
    {
        get
        {
            var values = FilterValues();
            values["page"] = Page.ToString(CultureInfo.InvariantCulture);
            return values;
        }
    }

    private Dictionary<string, string?> FilterValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture),
            ["status"] = Status,
        };
        if (EmployeeId is { } employeeId)
        {
            values["employeeId"] = employeeId.ToString(CultureInfo.InvariantCulture);
        }

        if (From is { } from)
        {
            values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return values;
    }
}
