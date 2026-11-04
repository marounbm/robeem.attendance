using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of the records waiting for review, and the filter that chose them.</summary>
public sealed class ReviewsQueueViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the reasons a record can wait for review, the first that applies.</summary>
    public static IReadOnlyList<string> Reasons { get; } = ["Overtime", "UnscheduledWork", "IncompleteDay", "Review"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the records on the page.</summary>
    public IReadOnlyList<ReviewListItem> Items { get; init; } = Array.Empty<ReviewListItem>();

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of records that match the filters, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets the one employee shown, or null for everyone the user may review.</summary>
    public int? EmployeeId { get; init; }

    /// <summary>Gets the employees the user can narrow the queue to.</summary>
    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Gets the reason shown, or null for any.</summary>
    public string? Why { get; init; }

    /// <summary>Gets what the pager needs: the page, and the filters that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    /// <summary>Gets what a decision sends back with it, so the queue is shown again as it was: the filters, the page size and the page.</summary>
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
        var values = new Dictionary<string, string?> { ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture) };
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

        if (!string.IsNullOrEmpty(Why))
        {
            values["why"] = Why;
        }

        return values;
    }
}
