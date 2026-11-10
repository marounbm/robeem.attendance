namespace robeem.Application.Attendance;

/// <summary>A device on a pulling or pushing connection from which no punch arrived in the look-back window.</summary>
/// <param name="DeviceId">The device.</param>
/// <param name="Name">Its name.</param>
/// <param name="Integration">The integration it belongs to.</param>
/// <param name="LastSeenAt">When the device last answered, if it ever did.</param>
public sealed record SilentDevice(int DeviceId, string Name, string Integration, DateTime? LastSeenAt);
