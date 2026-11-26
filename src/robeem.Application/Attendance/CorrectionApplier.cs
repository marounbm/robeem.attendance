using robeem.Application.Persistence;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Folds a correction request whose workflow is complete into its day: the approval becomes <c>Applied</c>, the exceptions it settles
/// are closed and the day is calculated again. Shared by an approver's decision and by a manager or HR proposing on an employee's behalf,
/// so the two end the same way.
/// </summary>
internal static class CorrectionApplier
{
    /// <summary>
    /// Applies the request. The caller owns the transaction: a day recalculated with the exceptions still open would never come out
    /// Approved, and a failure must not leave the correction applied to a day that was not recalculated.
    /// </summary>
    /// <param name="db">The unit of work.</param>
    /// <param name="audit">Where the audit entries are staged.</param>
    /// <param name="engine">Calculates the day again.</param>
    /// <param name="approval">The request's approval.</param>
    /// <param name="edits">The edits that point to the approval.</param>
    /// <param name="exceptions">The exceptions the request settles.</param>
    /// <param name="actor">Who is acting, as written in the audit log.</param>
    /// <param name="decidedBy">What the audit entry says about the decision, for example "Approved by Manager".</param>
    /// <param name="now">The local time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task ApplyAsync(
        IRobeemDbContext db,
        IAttendanceAuditTrail audit,
        IAttendanceProcessingEngine engine,
        AttendanceApproval approval,
        IEnumerable<AttendanceCorrection> edits,
        IEnumerable<AttendanceException> exceptions,
        string actor,
        string decidedBy,
        DateTime now,
        CancellationToken cancellationToken)
    {
        approval.Status = AttendanceApprovalStatus.Applied;
        approval.AppliedAt = now;
        foreach (var edit in edits)
        {
            edit.IsPending = false;
        }

        foreach (var exception in exceptions)
        {
            var before = exception.Status;
            exception.Status = AttendanceExceptionStatus.Closed;
            exception.ResolutionMethod = AttendanceExceptionResolutionMethod.Correction;
            exception.ResolvedAt = now;
            audit.Record(AttendanceAuditActions.ExceptionStatusChanged, nameof(AttendanceException), exception.Id, $"{before} -> Closed: the correction was applied.", actor);
        }

        audit.Record(AttendanceAuditActions.CorrectionDecided, nameof(AttendanceApproval), approval.Id, $"{decidedBy}; the correction is {approval.Status}.", actor);
        await db.SaveChangesAsync(cancellationToken);

        await engine.ProcessEmployeeDayAsync(approval.EmployeeId, approval.AttendanceDate, cancellationToken);
    }
}
