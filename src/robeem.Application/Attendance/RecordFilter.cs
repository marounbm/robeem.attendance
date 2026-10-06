using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>What the records list is narrowed to. Dates are inclusive; <paramref name="Take"/> is clamped to 1 to 500.</summary>
public sealed record RecordFilter(DateTime? From, DateTime? To, int? EmployeeId, AttendanceRecordStatus? Status, bool OnlyWithExceptions, int Take);
