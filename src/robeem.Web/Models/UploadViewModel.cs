using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>The upload screen: the integrations a file can go into, the one chosen with its format and layout, and one page of the files uploaded so far.</summary>
public sealed class UploadViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    public IReadOnlyList<UploadTarget> Targets { get; init; } = [];

    /// <summary>Gets the chosen target, or null when none is chosen yet.</summary>
    public UploadTarget? Selected { get; init; }

    /// <summary>Gets the files on the page, newest first.</summary>
    public IReadOnlyList<UploadedFileItem> Uploads { get; init; } = [];

    /// <summary>Gets the 1-based page of the history shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of files uploaded, on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets what the pager needs: the page, and what every page link keeps (the chosen integration and the page size).</summary>
    public PagerModel Pager => new()
    {
        Page = Page,
        PageSize = PageSize,
        Total = Total,
        RouteValues = RouteValues(),
    };

    private Dictionary<string, string?> RouteValues()
    {
        var values = new Dictionary<string, string?> { ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture) };
        if (Selected is { } selected)
        {
            values["integrationId"] = selected.IntegrationId.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }
}
