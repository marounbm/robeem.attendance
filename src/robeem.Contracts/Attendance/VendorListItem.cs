namespace robeem.Contracts.Attendance;

/// <summary>A vendor of the catalog, as the vendors screen lists it.</summary>
public sealed class VendorListItem
{
    public int Id { get; init; }

    public string VendorKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;

    public string WebsiteUrl { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>Gets how many connectors the vendor has.</summary>
    public int ConnectorCount { get; init; }
}
