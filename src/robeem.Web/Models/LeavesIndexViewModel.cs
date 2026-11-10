using System.Globalization;
using robeem.Contracts.HR.Leaves;

namespace robeem.Web.Models;

/// <summary>One page of leaves, and the filters that chose them.</summary>
public sealed class LeavesIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the leave types the type filter offers.</summary>
    public static IReadOnlyList<string> LeaveTypes { get; } = ["Annual", "Sick", "Unpaid", "Other"];

    public IReadOnlyList<LeaveListItem> Leaves { get; init; } = Array.Empty<LeaveListItem>();

    /// <summary>Gets the employee the list is limited to (from the Employees screen), if any.</summary>
    public int? EmployeeId { get; init; }

    public string? Search { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public string? LeaveType { get; init; }

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of leaves that match the filters, on every page together.</summary>
    public int Total { get; init; }

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
        if (EmployeeId is { } id)
        {
            values["employeeId"] = id.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search;
        }

        if (From is { } from)
        {
            values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(LeaveType))
        {
            values["type"] = LeaveType;
        }

        return values;
    }
}
