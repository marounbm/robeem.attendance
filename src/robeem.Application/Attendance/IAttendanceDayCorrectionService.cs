using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Correcting a whole day. The employee sees every punch of the day and sends, with one reason, the edits that put it right: a punch given
/// another type, a missing punch added, a punch that should not count ignored. The approvers decide the request as a whole, as for any
/// correction, and an approved request is folded into the day by the processing engine. Corrections are additive (CLAUDE.md §1.7): no
/// punch row is changed. The employee the day belongs to can see and correct it, and so can their manager and HR, on their behalf: a
/// person who sends a correction for someone else takes the steps of the approval they hold (nobody decides their own request), so it
/// may apply at once, or wait only for the step they do not hold.
/// </summary>
public interface IAttendanceDayCorrectionService
{
    /// <summary>
    /// The day's shifts, every punch with what can be done to it, and the exceptions to settle. Reading the punches is a direct read of
    /// normalized attendance, so it is audited, with who read them. Null when the employee does not exist or the viewer may not correct
    /// their day: the viewer is not the employee, their manager, or HR.
    /// </summary>
    Task<DayCorrectionView?> GetDayAsync(int employeeId, DateTime date, ExceptionViewer viewer, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the edits for approval. The day needs an open exception and no correction already waiting. Every edit is checked: the punch
    /// must be one of the day's that counts, a type must be a real one, an added punch must be on the day (or the day after, for a shift that ends
    /// after midnight) and not in the future. The day's open exceptions then wait for the decision. Nothing is saved when any edit is refused.
    /// Sent for someone else, a reason is required and the sender's own steps of the approval are taken (see the interface remarks); when
    /// that completes the workflow the correction is applied, the exceptions are closed and the day calculated again in the same transaction.
    /// </summary>
    Task<SaveResult> ProposeAsync(int employeeId, DayCorrectionRequest request, ExceptionViewer requestedBy, CancellationToken cancellationToken);
}
