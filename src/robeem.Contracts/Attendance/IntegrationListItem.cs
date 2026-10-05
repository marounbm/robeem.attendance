namespace robeem.Contracts.Attendance;

/// <summary>A tenant's connection to an attendance provider, as the integrations screen lists it.</summary>
public sealed class IntegrationListItem
{
    public int Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public int ProviderId { get; init; }

    public string ProviderKey { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Gets the configured status: Active, Paused or Failed.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the observed health: Unknown, Healthy, Degraded or Unhealthy. Kept by the sync job.</summary>
    public string HealthStatus { get; init; } = string.Empty;

    /// <summary>Gets the reference to the connection secret (never the secret itself).</summary>
    public string ConnectionConfigRef { get; init; } = string.Empty;

    public string? PollingScheduleCron { get; init; }

    public DateTime? LastSuccessfulSyncAt { get; init; }

    public int DeviceCount { get; init; }

    public int MappingCount { get; init; }
}
