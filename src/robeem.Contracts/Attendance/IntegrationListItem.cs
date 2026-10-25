namespace robeem.Contracts.Attendance;

/// <summary>A tenant's connection to an attendance vendor, as the integrations screen lists it.</summary>
public sealed class IntegrationListItem
{
    public int Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int ConnectorId { get; init; }

    public string ConnectorName { get; init; } = string.Empty;

    public string VendorKey { get; init; } = string.Empty;

    public string VendorName { get; init; } = string.Empty;

    /// <summary>Gets how the integration receives attendance: an AttendanceTransport name such as FileUpload.</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>Gets how the default connection is authenticated: a ConnectionAuthMethod name.</summary>
    public string AuthMethod { get; init; } = "None";

    /// <summary>Gets the integration's default connection; null for an integration saved before connections existed that has not been opened since.</summary>
    public int? DefaultConnectionId { get; init; }

    /// <summary>Gets how many connections the integration has.</summary>
    public int ConnectionCount { get; init; }

    /// <summary>Gets the transport's display name, as the administrators named it; set after the query, from the transport definitions.</summary>
    public string TransportName { get; set; } = string.Empty;

    /// <summary>Gets the file format uploads are read by; null for the connector's default.</summary>
    public int? PayloadFormatId { get; init; }

    public string? PayloadFormatName { get; init; }

    /// <summary>Gets the configured status: Active, Paused or Failed.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the observed health: Unknown, Healthy, Degraded or Unhealthy. Kept by the sync job.</summary>
    public string HealthStatus { get; init; } = string.Empty;

    /// <summary>Gets the reference to the connection secret (never the secret itself).</summary>
    public string ConnectionConfigRef { get; init; } = string.Empty;

    /// <summary>Gets the device every punch of an uploaded file is attributed to; null for none.</summary>
    public int? DefaultDeviceId { get; init; }

    public string? DefaultDeviceName { get; init; }

    /// <summary>Gets the IANA time zone used for punches with no UTC offset whose device is not registered; null for none.</summary>
    public string? TimeZoneId { get; init; }

    public string? PollingScheduleCron { get; init; }

    public DateTime? LastSuccessfulSyncAt { get; init; }

    public int DeviceCount { get; init; }

    public int MappingCount { get; init; }
}
