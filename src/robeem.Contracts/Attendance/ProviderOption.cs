namespace robeem.Contracts.Attendance;

public sealed class ProviderOption
{
    public int Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the company that makes the devices; empty when not recorded.</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Gets the manufacturer's website; empty when not recorded.</summary>
    public string WebsiteUrl { get; init; } = string.Empty;

    /// <summary>Gets the transports an integration of this provider can choose from: the provider's own, and file upload.</summary>
    public IReadOnlyList<TransportOption> SupportedTransports { get; init; } = Array.Empty<TransportOption>();

    /// <summary>Gets a value indicating whether the provider can be used: it is active, and has a default file format or a code adapter.</summary>
    public bool IsActive { get; init; }
}
