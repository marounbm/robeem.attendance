namespace robeem.Application.Attendance;

/// <summary>A device as the vendor describes it.</summary>
public sealed record ExternalDeviceDto(string ExternalDeviceId, string? Name = null, string? DeviceType = null, string? TimeZoneId = null);
