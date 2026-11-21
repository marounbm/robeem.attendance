using robeem.Application.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// The workflow on a group of related exceptions (one employee's shift on one date). A group is settled as a whole: it waits for a
/// correction of the day, or a manager or HR accepts it as it is. Each exception in it keeps its own status and is closed with the group.
/// </summary>
public interface IAttendanceExceptionGroupService
{
    /// <summary>
    /// Brings the day's groups in line with its exceptions: puts ungrouped exceptions in the group of their shift and sets each group's status
    /// from them. Changes no exception and does nothing when the groups are right.
    /// </summary>
    Task SyncDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken);

    /// <summary>
    /// Closes an open group as accepted, with the reason it is accepted for. Only when every exception still open in it is a kind that
    /// can be accepted (a break exception, a no-show, a day worked short); a missing punch is corrected. The employee's manager or HR can do
    /// it, never the employee. Every exception is closed as accepted with the same reason, audited, and the day is calculated again. Not
    /// found when there is no such group.
    /// </summary>
    Task<SaveResult> AcceptAsync(int groupId, int acceptedByEmployeeId, bool asHr, string? reason, CancellationToken cancellationToken);
}
