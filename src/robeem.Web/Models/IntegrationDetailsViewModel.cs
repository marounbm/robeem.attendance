using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

public sealed class IntegrationDetailsViewModel
{
    public static IReadOnlyList<string> DeviceTypes { get; } = ["Biometric", "Facial", "RFID", "Mobile", "Cloud", "OnPrem"];

    /// <summary>Gets IANA zones offered as suggestions; any IANA zone id is accepted.</summary>
    public static IReadOnlyList<string> CommonTimeZones { get; } =
    [
        "UTC", "Europe/London", "Europe/Paris", "Europe/Istanbul", "Asia/Beirut", "Asia/Dubai", "Asia/Riyadh", "Africa/Cairo",
        "Asia/Kolkata", "Asia/Singapore", "Australia/Sydney", "America/New_York", "America/Chicago", "America/Los_Angeles",
    ];

    public required IntegrationListItem Integration { get; init; }

    public IntegrationHealthReport? Health { get; init; }

    public IReadOnlyList<DeviceItem> Devices { get; init; } = Array.Empty<DeviceItem>();

    public IReadOnlyList<MappingItem> Mappings { get; init; } = Array.Empty<MappingItem>();

    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();
}
