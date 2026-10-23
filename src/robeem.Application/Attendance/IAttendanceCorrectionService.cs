using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The correction workflow of study §11: an employee proposes the missing punch of an open
/// exception, the approvers the policy names decide, and an approved correction is folded into the
/// day by the processing engine. Corrections are additive (CLAUDE.md §1.7): no punch row is
/// changed.
/// </summary>
public interface IAttendanceCorrectionService
{
    /// <summary>
    /// Proposes the missing punch of an open <c>MissingPunch</c> exception, at
    /// <paramref name="proposedValue"/> (local wall-clock time). The correction is Pending and the
    /// exception moves to PendingCorrection. Only the exception's own employee can propose.
    /// </summary>
    Task<CorrectionResult> ProposeCorrectionAsync(int exceptionId, DateTime proposedValue, string justification, int requestedByEmployeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Proposes giving the out-of-order punch of an open <c>InvalidSequence</c> exception another type (for example, a check-out
    /// that follows a check-out was really a check-in). Only a type the exception lists as settling it can be proposed. The correction is
    /// Pending and the exception moves to PendingCorrection; once approved, the engine reads the same punch with its new type, and
    /// the normalized punch is never edited. Only the exception's own employee can propose.
    /// </summary>
    Task<CorrectionResult> ProposeTypeChangeAsync(int exceptionId, string newEventType, string justification, int requestedByEmployeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the manager's or HR's decision, whichever <paramref name="isHrDecision"/> says. The
    /// correction is Applied only once every approval the policy asks for is in (Manager, HR, or
    /// the manager and then HR); then the day is recalculated with the corrected punch and the
    /// exception closed. A rejection by anyone rejects the correction and reopens the exception.
    /// The manager's step belongs to the employee's own manager; HR may take it only when the
    /// employee has no manager or an inactive one (then <paramref name="isHrDecision"/> true on a
    /// correction still awaiting its manager step is recorded as that step).
    /// </summary>
    Task<CorrectionResult> DecideCorrectionAsync(int correctionId, int decidedByUserId, bool approved, bool isHrDecision, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the pending corrections whose next decision is <paramref name="approver"/>'s, oldest
    /// first. A manager (<paramref name="approverEmployeeId"/>) gets their direct reports'; HR gets
    /// every HR-step one, plus the manager-step ones of employees who have no active manager.
    /// </summary>
    Task<IReadOnlyList<CorrectionListItem>> ListPendingAsync(CorrectionApprover approver, int approverEmployeeId, CancellationToken cancellationToken);
}
