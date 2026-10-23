using System.Globalization;

namespace robeem.Web.Models;

/// <summary>What the shared pager shows: where the page is in the list, and the filters every page link must keep.</summary>
public sealed class PagerModel
{
    private const int Neighbours = 2;

    /// <summary>Gets the 1-based page shown.</summary>
    public required int Page { get; init; }

    public required int PageSize { get; init; }

    /// <summary>Gets the number of items on every page together.</summary>
    public required int Total { get; init; }

    /// <summary>Gets the query-string values every page link carries besides <c>page</c>: the filters and the page size.</summary>
    public required IReadOnlyDictionary<string, string?> RouteValues { get; init; }

    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    /// <summary>Gets the 1-based position of the first item on the page; 0 for an empty list.</summary>
    public int FirstItem => Total == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItem => Math.Min(Page * PageSize, Total);

    /// <summary>Gets the pages to link to: the first, the last and those next to the current one.</summary>
    public IReadOnlyList<int> Pages =>
        Enumerable.Range(1, PageCount)
            .Where(p => p == 1 || p == PageCount || Math.Abs(p - Page) <= Neighbours)
            .ToList();

    /// <summary>Gets the route values of a link to a page.</summary>
    public IDictionary<string, string?> For(int page) =>
        new Dictionary<string, string?>(RouteValues) { ["page"] = page.ToString(CultureInfo.InvariantCulture) };
}
