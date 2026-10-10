using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>An integration's raw punches beside the normalized punches they produced, and how its provider maps them.</summary>
public sealed class IntegrationRawMappingViewModel
{
    public static IReadOnlyList<string> Statuses { get; } = ["Pending", "Normalized", "Failed", "Skipped"];

    public required IntegrationListItem Integration { get; init; }

    public required ProviderMappingGuide Guide { get; init; }

    public IReadOnlyList<RawNormalizedMappingItem> Rows { get; init; } = Array.Empty<RawNormalizedMappingItem>();

    public int? BatchId { get; init; }

    public string? Status { get; init; }

    public int Take { get; init; }
}
