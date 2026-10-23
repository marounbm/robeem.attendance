using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// Looking at calculated attendance. An employee sees their own days, a manager their direct
/// reports', HR everyone's (the same scope as <see cref="IAttendanceExceptionQuery"/>). Reading a
/// day's punches is a direct read of normalized attendance, so it is audited.
/// </summary>
public interface IAttendanceRecordService
{
    /// <summary>
    /// Lists one page of the days the viewer may see, newest first, with the number of days that match. A page past
    /// the end is answered with the last page.
    /// </summary>
    Task<PagedResult<RecordDayListItem>> ListDaysAsync(RecordFilter filter, ExceptionViewer viewer, CancellationToken cancellationToken);

    /// <summary>Lists the employees whose days the viewer may see, by name, to filter the list by one of them.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(ExceptionViewer viewer, CancellationToken cancellationToken);

    /// <summary>One employee's day in full, or null when the employee doesn't exist or the viewer may not see them.</summary>
    Task<RecordDayDetail?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the processing engine again for the day, which picks up a changed policy, schedule or late
    /// punch. A changed result is a new version; nothing is rewritten. Null when the employee doesn't exist.
    /// </summary>
    Task<ReprocessResponse?> ReprocessAsync(int employeeId, DateTime date, CancellationToken cancellationToken);
}
