using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

internal sealed record SeedTransport(AttendanceTransport Transport, string DisplayName, bool IsPull, bool UsesPayloadFormat);
