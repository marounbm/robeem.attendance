using robeem.DataModel;

namespace robeem.Application.Attendance.Seeding;

internal sealed record SeedFormat(
    string Name,
    string Description,
    string EmployeeColumn,
    string TimestampColumn,
    string TypeColumn,
    string DirectionColumn,
    string DeviceColumn,
    string TransactionIdColumn,
    string PayloadColumns,
    string[] TimestampFormats,
    (string Code, NormalizedEventType Type)[] EventCodes);
