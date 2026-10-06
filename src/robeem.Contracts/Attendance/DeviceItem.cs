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

    /// <summary>Gets the machine model, e.g. U350; empty when not recorded.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the version of the vendor's attendance software that collects from the device; empty when not recorded.</summary>
    public string SoftwareVersion { get; init; } = string.Empty;

    /// <summary>Gets the version of the vendor SDK the software uses; empty when not recorded.</summary>
    public string SdkVersion { get; init; } = string.Empty;

    /// <summary>Gets the IANA time zone the device's local punch times are in, e.g. Asia/Beirut.</summary>
    public string TimeZoneId { get; init; } = string.Empty;

    public DateTime? LastSeenAt { get; init; }
}
