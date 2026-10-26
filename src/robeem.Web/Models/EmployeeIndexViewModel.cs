using System.Globalization;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.Models;

/// <summary>One page of the employees, and the search that chose them.</summary>
public sealed class EmployeeIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the employees on the page.</summary>
    public IReadOnlyList<EmployeeListItem> Employees { get; init; } = Array.Empty<EmployeeListItem>();

    public string? Search { get; init; }

    public bool IncludeInactive { get; init; }

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of employees that match the search, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets what the pager needs: the page, and the search that every page link keeps.</summary>
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
        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search;
        }

        if (IncludeInactive)
        {
            values["includeInactive"] = "true";
        }

        return values;
    }
}
