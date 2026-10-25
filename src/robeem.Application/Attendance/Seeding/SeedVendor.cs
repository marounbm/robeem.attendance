using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

/// <summary>A built-in vendor and its one connector, which carries the vendor's key: what it is read by and the transports it supports.</summary>
internal sealed record SeedVendor(
    string Key,
    string DisplayName,
    AttendanceTransport[] Transports,
    bool SupportsIncrementalSync,
    bool IsActive,
    string? DefaultFormat = null,
    string Manufacturer = "",
    string WebsiteUrl = "",
    string? AdapterKey = null);
