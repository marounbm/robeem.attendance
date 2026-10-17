namespace robeem.Contracts.Attendance;

/// <summary>A kind of punch device, as the device types screen lists it.</summary>
public sealed class DeviceTypeItem
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    /// <summary>Gets how many devices have this type.</summary>
    public int DeviceCount { get; init; }
}
