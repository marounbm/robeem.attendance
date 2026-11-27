using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The compensation overview: one row per employee, for each one's own pay period.</summary>
public sealed class CompensationIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>The period of each employee's latest day with attendance data: the default, so imported history is found.</summary>
    public const string Latest = "Latest";

    /// <summary>Each employee's most recently finished period, counted from today.</summary>
    public const string Last = "Last";

    /// <summary>The period that contains a chosen date.</summary>
    public const string Containing = "Containing";

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    public IReadOnlyList<CompensationOverviewItem> Items { get; init; } = Array.Empty<CompensationOverviewItem>();

    public string? Search { get; init; }

    /// <summary>Gets <see cref="Latest"/>, <see cref="Last"/> or <see cref="Containing"/>.</summary>
    public string Mode { get; init; } = Latest;

    /// <summary>Gets the date whose period is shown, when <see cref="Mode"/> is <see cref="Containing"/>.</summary>
    public DateTime? Date { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int Total { get; init; }

    /// <summary>Gets what the pager needs, with the mode, the date and the search that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    /// <summary>Gets the values that keep the same view when opening one employee's report.</summary>
    public IDictionary<string, string?> PeriodValues() => FilterValues();

    private Dictionary<string, string?> FilterValues()
    {
        var values = new Dictionary<string, string?> { ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture), ["mode"] = Mode };
        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search;
        }

        if (Mode == Containing && Date is { } date)
        {
            values["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return values;
    }
}
