using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// How a record the engine left <c>PendingReview</c> gets approved or rejected: overtime that
/// needs pre-approval, work outside any shift, a day with a punch missing and no exception left
/// open. The decision is an additive row (CLAUDE.md §1.7); only the record's review status moves.
/// </summary>
public interface IAttendanceReviewService
{
    /// <summary>
    /// Approves or rejects a current <c>PendingReview</c> record. Refused when: the record is not
    /// current or not waiting; an exception of that employee, date and shift is still open (settle it
    /// first); the reviewer is the record's own employee; a manager is not that employee's manager
    /// (HR may review anyone); a rejection has no reason. The decision, the status change, the audit
    /// entry and the recalculation of the day's summary are one transaction. If the day turns out to
    /// have changed under the reviewer (the recalculation writes a new version), nothing is kept and
    /// the reviewer is asked to review again.
    /// </summary>
    Task<ReviewResult> DecideAsync(int recordId, int decidedByEmployeeId, bool asHr, bool approved, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the current records waiting for review, with no exception open, oldest date first. A
    /// manager gets their direct reports'; HR gets every employee's.
    /// </summary>
    Task<IReadOnlyList<ReviewListItem>> ListPendingAsync(int approverEmployeeId, bool isHr, CancellationToken cancellationToken);
}
