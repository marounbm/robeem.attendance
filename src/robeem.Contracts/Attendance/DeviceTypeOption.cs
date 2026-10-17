namespace robeem.Contracts.Attendance;

/// <summary>An active kind of punch device, to choose for a device.</summary>
public sealed class DeviceTypeOption
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
