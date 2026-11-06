using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of what waits for the approver's decision (corrections and reviews together), and the filter that chose it.</summary>
public sealed class ApprovalsQueueViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the kinds of approval.</summary>
    public static IReadOnlyList<string> Types { get; } = ["Correction", "Review"];

    /// <summary>Gets the reasons a record can wait for review, the first that applies.</summary>
    public static IReadOnlyList<string> Reasons { get; } = ["Overtime", "UnscheduledWork", "IncompleteDay", "Review"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the approvals on the page.</summary>
    public IReadOnlyList<ApprovalListItem> Items { get; init; } = Array.Empty<ApprovalListItem>();

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of approvals that match the filters, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets the one employee shown, or null for everyone the user may decide for.</summary>
    public int? EmployeeId { get; init; }

    /// <summary>Gets the employees the user can narrow the queue to.</summary>
    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Gets the kind shown, or null for both.</summary>
    public string? Type { get; init; }

    /// <summary>Gets the review reason shown, or null for any.</summary>
    public string? Why { get; init; }

    /// <summary>Gets a value indicating whether any filter narrows the queue.</summary>
    public bool IsFiltered => EmployeeId is not null || From is not null || To is not null || Type is not null || Why is not null;

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

        if (!string.IsNullOrEmpty(Type))
        {
            values["type"] = Type;
        }

        if (!string.IsNullOrEmpty(Why))
        {
            values["why"] = Why;
        }

        return values;
    }
}
