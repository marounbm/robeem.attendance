using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

internal sealed record SeedProvider(
    string Key,
    string DisplayName,
    AttendanceTransport[] Transports,
    bool SupportsWebhooks,
    bool SupportsIncrementalSync,
    bool IsActive,
    string? DefaultFormat = null,
    string Manufacturer = "",
    string WebsiteUrl = "");
