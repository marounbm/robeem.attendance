namespace robeem.Contracts.Attendance;

/// <summary>An active kind of punch device, to choose for a device. A device refers to its type by name.</summary>
public sealed class DeviceTypeOption
{
    public string Name { get; init; } = string.Empty;
}
