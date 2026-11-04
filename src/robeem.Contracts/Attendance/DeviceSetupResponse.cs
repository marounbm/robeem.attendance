namespace robeem.Contracts.Attendance;

/// <summary>Body of a <c>201 Created</c> answer to setting a device up: the device, and the integration it was put in.</summary>
public sealed class DeviceSetupResponse
{
    public int DeviceId { get; init; }

    public int IntegrationId { get; init; }

    /// <summary>Gets a value indicating whether the connector had no integration yet, so one was created.</summary>
    public bool IntegrationCreated { get; init; }

    /// <summary>Gets a value indicating whether a new connection was made for the device.</summary>
    public bool ConnectionCreated { get; init; }
}
