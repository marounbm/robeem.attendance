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

    /// <summary>Gets the column the list is sorted by; without a choice, the employee number.</summary>
    public EmployeeSortField Sort { get; init; } = EmployeeSortField.Number;

    /// <summary>Gets a value indicating whether the sort runs from the highest to the lowest.</summary>
    public bool Descending { get; init; }

    /// <summary>Gets what the pager needs: the page, and the search and sort that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = PagerValues(),
    };

    /// <summary>Gets the name a column has in the address.</summary>
    public static string SortName(EmployeeSortField field) => field.ToString().ToLowerInvariant();

    /// <summary>
    /// Gets the route values of a column heading's link: the search and page size as they are, no page (sorting starts again at the first),
    /// and the column. Choosing the column the list is already sorted by, ascending, turns it round; any other starts ascending.
    /// </summary>
    public Dictionary<string, string?> SortLink(EmployeeSortField field)
    {
        var values = FilterValues();
        values["sort"] = SortName(field);
        values["dir"] = Sort == field && !Descending ? "desc" : "asc";
        return values;
    }

    /// <summary>Gets the sort mark for a column heading, for <c>aria-sort</c>: <c>ascending</c>, <c>descending</c> or <c>none</c>.</summary>
    public string AriaSort(EmployeeSortField field) => Sort != field ? "none" : Descending ? "descending" : "ascending";

    private Dictionary<string, string?> PagerValues()
    {
        var values = FilterValues();
        if (Sort != EmployeeSortField.Number || Descending)
        {
            values["sort"] = SortName(Sort);
            values["dir"] = Descending ? "desc" : "asc";
        }

        return values;
    }

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
