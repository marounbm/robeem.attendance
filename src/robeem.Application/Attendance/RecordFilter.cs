using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What the attendance days list is narrowed to, which page of it is asked for (1-based), and the column it is sorted by.
/// Without a sort it runs from the oldest day to the newest.
/// </summary>
public sealed record RecordFilter(
    DateTime? From,
    DateTime? To,
    int? EmployeeId,
    AttendanceRecordStatus? Status,
    bool OnlyWithExceptions,
    int Page,
    int PageSize,
    RecordSortField Sort = RecordSortField.Date,
    bool Descending = false);
