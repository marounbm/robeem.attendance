namespace robeem.Contracts.Attendance;

/// <summary>A device type of the catalog, as the prime Api sends it.</summary>
public sealed class CatalogDeviceType
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
