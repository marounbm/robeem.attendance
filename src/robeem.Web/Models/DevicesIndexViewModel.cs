using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class DevicesIndexViewModel
{
    public IReadOnlyList<DeviceItem> Devices { get; init; } = Array.Empty<DeviceItem>();

    /// <summary>Gets the integrations the grid can be narrowed to.</summary>
    public IReadOnlyList<IntegrationListItem> Integrations { get; init; } = Array.Empty<IntegrationListItem>();

    /// <summary>Gets the integration the grid is narrowed to; null for every integration.</summary>
    public int? IntegrationId { get; init; }
}
