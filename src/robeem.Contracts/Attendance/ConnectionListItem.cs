namespace robeem.Contracts.Attendance;

/// <summary>One connection of an integration, as the connections screen lists it. Carries no credential, only the reference to it.</summary>
public sealed class ConnectionListItem
{
    public int Id { get; init; }

    public int IntegrationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    /// <summary>Gets the transport's key (an <c>AttendanceTransport</c> name).</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>Gets the tenant's name for the transport.</summary>
    public string TransportName { get; set; } = string.Empty;

    public string AuthMethod { get; init; } = "None";

    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    public string SecretRef { get; init; } = string.Empty;

    public string? PollingSchedule { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Gets how many devices name this connection (not counting those that use it as the default).</summary>
    public int DeviceCount { get; init; }

    public DateTime? LastTestedAt { get; init; }

    public bool? LastTestSucceeded { get; init; }

    public string? LastTestMessage { get; init; }
}
