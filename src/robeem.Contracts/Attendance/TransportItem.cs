namespace robeem.Contracts.Attendance;

/// <summary>A transport definition, as the transports screen lists it.</summary>
public sealed class TransportItem
{
    public int Id { get; init; }

    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool IsPull { get; init; }

    public bool UsesFileFormat { get; init; }

    public bool IsActive { get; init; }

    /// <summary>Gets how many integrations receive attendance this way.</summary>
    public int IntegrationCount { get; init; }

    /// <summary>Gets how many providers list it as a transport.</summary>
    public int ProviderCount { get; init; }
}
