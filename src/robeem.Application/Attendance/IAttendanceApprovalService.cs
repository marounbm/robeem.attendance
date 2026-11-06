using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// The one approval workflow: what an approver has to decide, whether it is a correction a person asked for or a record the engine
/// left for review, and the decision itself. The correction and review services are wrappers over this one for their older callers.
/// </summary>
public interface IAttendanceApprovalService
{
    /// <summary>
    /// Lists what waits for this approver, oldest day first, a page at a time. A manager (<paramref name="isManager"/>) gets their direct
    /// reports'; HR (<paramref name="isHr"/>) gets every review and the corrections whose next step is HR's. Narrowed by employee,
    /// attendance dates, kind and review reason. The total is counted with the same filters as the page, the order is stable (date,
    /// then id) so pages never overlap, a page past the end answers the last page, and the page size is clamped to 1..200
    /// (0 means the default, 20).
    /// </summary>
    Task<PagedResult<ApprovalListItem>> ListPendingAsync(int approverEmployeeId, bool isManager, bool isHr, ApprovalFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Approves or rejects a pending approval as the manager or as HR (<paramref name="asHr"/>). A correction is applied once every step
    /// its workflow asks for is in (the edits are folded into the day, the exceptions closed, the day recalculated, in one transaction);
    /// a rejection by anyone rejects it and reopens the exceptions. A review is decided in one step (the record's status moves and the day
    /// is recalculated, in one transaction; if the day changed under the reviewer nothing is kept and they are asked to review again).
    /// Refused when: it is not pending; the decider is the person who asked or the record's own employee; the role is not the one
    /// whose step it is; a manager is not that employee's manager (HR may take the step of an employee with no active manager, and
    /// may review anyone); a review's record has an exception still open; a rejected review has no reason.
    /// </summary>
    Task<ApprovalResult> DecideAsync(int approvalId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken);
}
