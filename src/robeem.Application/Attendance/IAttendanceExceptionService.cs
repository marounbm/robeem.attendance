using robeem.Application.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// Settling an attendance exception that has no punch to supply. A break exception (long, late or early) is not fixed by
/// adding a punch, so a manager or HR accepts it, with a reason; the day is then calculated again and can be approved.
/// A missing punch is not accepted: it is corrected (<see cref="IAttendanceCorrectionService"/>).
/// </summary>
public interface IAttendanceExceptionService
{
    /// <summary>
    /// Closes an open break exception as accepted. The employee's manager or HR can do it, never the employee, and a reason is required.
    /// Audited; the day is recalculated. Not found when there is no such exception.
    /// </summary>
    Task<SaveResult> AcceptAsync(int exceptionId, int acceptedByEmployeeId, bool asHr, string? reason, CancellationToken cancellationToken);
}
