using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>What the attendance days list is narrowed to, and which page of it is asked for (1-based).</summary>
public sealed record RecordFilter(DateTime? From, DateTime? To, int? EmployeeId, AttendanceRecordStatus? Status, bool OnlyWithExceptions, int Page, int PageSize);
