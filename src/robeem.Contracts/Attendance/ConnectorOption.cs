namespace robeem.Contracts.Attendance;

/// <summary>A connector an integration can be made with, as the integration form lists it.</summary>
public sealed class ConnectorOption
{
    public int Id { get; init; }

    /// <summary>Gets the connector's key.</summary>
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string VendorKey { get; init; } = string.Empty;

    public string VendorName { get; init; } = string.Empty;

    /// <summary>Gets the company that makes the devices; empty when not recorded.</summary>
    public string Manufacturer { get; init; } = string.Empty;

    /// <summary>Gets the manufacturer's website; empty when not recorded.</summary>
    public string WebsiteUrl { get; init; } = string.Empty;

    /// <summary>Gets the transports an integration of this connector can choose from: the connector's own, and file upload.</summary>
    public IReadOnlyList<TransportOption> SupportedTransports { get; init; } = Array.Empty<TransportOption>();

    /// <summary>Gets a value indicating whether the connector can be used: it and its vendor are active, and something can read what it delivers.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets how the connector's payload is encrypted: None, Pgp, Aes or PasswordProtected. The integration then names the key.</summary>
    public string EncryptionKind { get; init; } = "None";
}
