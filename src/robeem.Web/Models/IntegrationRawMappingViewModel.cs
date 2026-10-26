using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of an integration's raw punches beside the normalized punches they produced, and how its vendor maps them.</summary>
public sealed class IntegrationRawMappingViewModel
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<string> Statuses { get; } = ["Pending", "Normalized", "Failed", "Skipped"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    public required IntegrationListItem Integration { get; init; }

    public required VendorMappingGuide Guide { get; init; }

    public IReadOnlyList<RawNormalizedMappingItem> Rows { get; init; } = Array.Empty<RawNormalizedMappingItem>();

    public int? BatchId { get; init; }

    public string? Status { get; init; }

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of rows that match the filters, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets what the pager needs: the page, and the integration and filters that every page link keeps.</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = FilterValues(),
    };

    private Dictionary<string, string?> FilterValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["id"] = Integration.Id.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (BatchId is { } batchId)
        {
            values["batchId"] = batchId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Status))
        {
            values["status"] = Status;
        }

        return values;
    }
}
