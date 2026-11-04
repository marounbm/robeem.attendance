namespace robeem.Contracts.Attendance;

/// <summary>A connector of the catalog with its vendor, as the prime Api sends it. Whether it is usable is decided by the reader, from the adapters and decryptors it has.</summary>
public sealed class CatalogConnector
{
    public int Id { get; init; }

    public string ConnectorKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public int VendorId { get; init; }

    public string VendorKey { get; init; } = string.Empty;

    public string VendorName { get; init; } = string.Empty;

    public string VendorManufacturer { get; init; } = string.Empty;

    public string VendorWebsiteUrl { get; init; } = string.Empty;

    public bool VendorIsActive { get; init; }

    public bool IsActive { get; init; }

    public string? AdapterKey { get; init; }

    public bool SupportsIncrementalSync { get; init; }

    /// <summary>Gets the name of a <c>PayloadEncryptionKind</c>.</summary>
    public string EncryptionKind { get; init; } = string.Empty;

    public string? DefaultPayloadFormatName { get; init; }

    public List<string> Transports { get; init; } = [];
}
