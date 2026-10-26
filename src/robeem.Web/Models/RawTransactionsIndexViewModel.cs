using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of the raw punches, exactly as the vendors sent them, and the filter that chose them.</summary>
public sealed class RawTransactionsIndexViewModel
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<string> Statuses { get; } = ["Pending", "Normalized", "Failed", "Skipped"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the punches on the page.</summary>
    public IReadOnlyList<RawTransactionListItem> Rows { get; init; } = Array.Empty<RawTransactionListItem>();

    public IReadOnlyList<TransactionIntegrationOption> Integrations { get; init; } = Array.Empty<TransactionIntegrationOption>();

    public int? IntegrationId { get; init; }

    public int? BatchId { get; init; }

    public string? Status { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public string? Search { get; init; }

    /// <summary>Gets part of the employee's number or name the list is narrowed to.</summary>
    public string? Employee { get; init; }

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of punches that match the filters, on every page together.</summary>
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
        if (IntegrationId is { } integrationId)
        {
            values["integrationId"] = integrationId.ToString(CultureInfo.InvariantCulture);
        }

        if (BatchId is { } batchId)
        {
            values["batchId"] = batchId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(Status))
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

        if (!string.IsNullOrWhiteSpace(Search))
        {
            values["search"] = Search;
        }

        if (!string.IsNullOrWhiteSpace(Employee))
        {
            values["employee"] = Employee;
        }

        return values;
    }
}
