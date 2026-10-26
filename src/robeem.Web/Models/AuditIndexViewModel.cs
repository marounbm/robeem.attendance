using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One page of the audit log, and the filter that chose it.</summary>
public sealed class AuditIndexViewModel
{
    public const int DefaultPageSize = 20;

    /// <summary>Gets the page sizes the user can choose from.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [10, DefaultPageSize, 50, 100, 200];

    /// <summary>Gets the entries on the page.</summary>
    public IReadOnlyList<AuditLogEntry> Entries { get; init; } = Array.Empty<AuditLogEntry>();

    public string? Action { get; init; }

    public string? EntityType { get; init; }

    public int? EntityId { get; init; }

    /// <summary>Gets the 1-based page shown.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Gets the number of entries that match the filters, on every page together.</summary>
    public int Total { get; init; }

    public IReadOnlyList<string> Actions => AuditActionCatalog.All;

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
        if (!string.IsNullOrWhiteSpace(Action))
        {
            values["action"] = Action;
        }

        if (!string.IsNullOrWhiteSpace(EntityType))
        {
            values["entityType"] = EntityType;
        }

        if (EntityId is { } entityId)
        {
            values["entityId"] = entityId.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }
}
