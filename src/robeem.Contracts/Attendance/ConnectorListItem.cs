namespace robeem.Contracts.Attendance;

/// <summary>A connector of the catalog, as the connectors screen lists it.</summary>
public sealed class ConnectorListItem
{
    public int Id { get; init; }

    public int VendorId { get; init; }

    public string VendorName { get; init; } = string.Empty;

    public string ConnectorKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? AdapterKey { get; init; }

    /// <summary>Gets the AttendanceTransport names the connector supports besides file upload.</summary>
    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();

    public int? DefaultPayloadFormatId { get; init; }

    public string? DefaultPayloadFormatName { get; init; }

    /// <summary>Gets the kinds of payload (Csv, Excel, ...) of the connector's default format and of the formats made for it.</summary>
    public IReadOnlyList<string> PayloadKinds { get; init; } = Array.Empty<string>();

    public bool SupportsIncrementalSync { get; init; }

    public string EncryptionKind { get; init; } = "None";

    public bool IsActive { get; init; }

    /// <summary>Gets a value indicating whether the code has an adapter registered under the connector's adapter key.</summary>
    public bool HasAdapter { get; init; }

    /// <summary>Gets a value indicating whether an integration can use the connector: it and its vendor are active, and it has a default payload format or an adapter.</summary>
    public bool IsUsable { get; init; }

    public int IntegrationCount { get; init; }
}
