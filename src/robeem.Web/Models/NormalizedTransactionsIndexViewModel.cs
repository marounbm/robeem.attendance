using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of the normalized punches, interpreted from the raw ones, and the filter that chose them.</summary>
public sealed class NormalizedTransactionsIndexViewModel
{
    public const int DefaultPageSize = 20;

    public static IReadOnlyList<string> EventTypes { get; } = ["CheckIn", "CheckOut", "BreakOut", "BreakIn", "Unknown", "Other"];

    public static IReadOnlyList<string> DuplicateStatuses { get; } = ["Unique", "DuplicateConfirmed", "DuplicateCandidate"];

    public static IReadOnlyList<string> Flags { get; } = ["None", "FlaggedInvalidSequence", "FlaggedUnrecognizedEmployee", "FlaggedUnrecognizedDevice"];

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the punches on the page.</summary>
    public IReadOnlyList<NormalizedTransactionListItem> Rows { get; init; } = Array.Empty<NormalizedTransactionListItem>();

    public IReadOnlyList<TransactionIntegrationOption> Integrations { get; init; } = Array.Empty<TransactionIntegrationOption>();

    public int? IntegrationId { get; init; }

    public int? RawId { get; init; }

    public string? Employee { get; init; }

    public string? EventType { get; init; }

    public string? Duplicate { get; init; }

    public string? Flag { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

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

        if (RawId is { } rawId)
        {
            values["rawId"] = rawId.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(Employee))
        {
            values["employee"] = Employee;
        }

        if (!string.IsNullOrEmpty(EventType))
        {
            values["eventType"] = EventType;
        }

        if (!string.IsNullOrEmpty(Duplicate))
        {
            values["duplicate"] = Duplicate;
        }

        if (!string.IsNullOrEmpty(Flag))
        {
            values["flag"] = Flag;
        }

        if (From is { } from)
        {
            values["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is { } to)
        {
            values["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return values;
    }
}
