namespace robeem.Contracts.Attendance;

/// <summary>A transport definition of the catalog, as the prime Api sends it.</summary>
public sealed class CatalogTransport
{
    public int Id { get; init; }

    public string TransportKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool IsPull { get; init; }

    public bool UsesPayloadFormat { get; init; }

    public bool IsActive { get; init; }
}
