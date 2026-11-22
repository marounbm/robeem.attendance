using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// What the attendance days list is narrowed to, which page of it is asked for (1-based), and the column it is sorted by.
/// Without a sort it runs from the oldest day to the newest. <c>UnderTime</c> true keeps only the days shown as <i>Under timed</i> (approved, with time
/// worked short of the schedule); false leaves those out; null does not look at it.
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
    bool Descending = false,
    bool? UnderTime = null);
