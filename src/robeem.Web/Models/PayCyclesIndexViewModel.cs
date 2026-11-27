using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class PayCyclesIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the default cycle as it applies, with the periods around today.</summary>
    public required PayCycleItem Default { get; init; }

    public IReadOnlyList<EmployeePayCycleItem> Employees { get; init; } = Array.Empty<EmployeePayCycleItem>();

    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int Total { get; init; }

    /// <summary>Gets the form values for the default cycle, from what applies now.</summary>
    public PayCycleInput DefaultInput => new()
    {
        Frequency = Default.Frequency,
        AnchorDate = Default.AnchorDate.Year < 2000 ? new PayCycleInput().AnchorDate : Default.AnchorDate,
        StartDay = Default.StartDay,
    };

    /// <summary>Gets what the pager needs, with the search and the page size that every page link keeps.</summary>
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

        return values;
    }
}
