namespace robeem.Contracts.Attendance;

/// <summary>A provider of the catalog, as the providers screen lists it.</summary>
public sealed class ProviderListItem
{
    public int Id { get; init; }

    public string ProviderKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    public string WebsiteUrl { get; init; } = string.Empty;

    /// <summary>Gets the AttendanceTransport names the provider supports besides file upload.</summary>
    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    public int? DefaultFileFormatId { get; init; }

    public string? DefaultFileFormatName { get; init; }

    public bool SupportsWebhooks { get; init; }

    public bool SupportsIncrementalSync { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Gets a value indicating whether the code has an adapter registered under the provider's key (pulling from an API, receiving webhooks).</summary>
    public bool HasAdapter { get; init; }

    /// <summary>Gets a value indicating whether an integration can use the provider: it is active, and it has a default file format or an adapter.</summary>
    public bool IsUsable { get; init; }

    public int IntegrationCount { get; init; }
}
