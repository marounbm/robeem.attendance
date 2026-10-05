namespace robeem.Contracts.Attendance;

/// <summary>A punch device of an integration.</summary>
public sealed class DeviceItem
{
    public int Id { get; init; }

    /// <summary>Gets the id the vendor reports for the device. Unique within the integration.</summary>
    public string ExternalDeviceId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the kind: Biometric, Facial, RFID, Mobile, Cloud or OnPrem.</summary>
    public string DeviceType { get; init; } = string.Empty;

    /// <summary>Gets the IANA time zone the device's local punch times are in, e.g. Asia/Beirut.</summary>
    public string TimeZoneId { get; init; } = string.Empty;

    public DateTime? LastSeenAt { get; init; }
}
