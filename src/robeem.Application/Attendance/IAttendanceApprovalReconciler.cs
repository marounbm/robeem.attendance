namespace robeem.Application.Attendance;

/// <summary>
/// The safety net under the review approvals: the engine queues one for every record it leaves for review and sets it aside when the
/// record changes, and this puts right any drift between the two (a record written by a path that skipped the engine, a failure
/// between two saves). Nothing a person decided is ever touched.
/// </summary>
public interface IAttendanceApprovalReconciler
{
    /// <summary>
    /// Sets aside (Superseded) every pending review approval whose record is no longer current or no longer waiting for review, and queues
    /// a pending review approval for every current record waiting for review that has none. Returns what it changed.
    /// </summary>
    Task<ApprovalReconciliation> ReconcileAsync(CancellationToken cancellationToken);
}

/// <summary>What <see cref="IAttendanceApprovalReconciler"/> changed.</summary>
/// <param name="Queued">Pending review approvals it added for records waiting for review.</param>
/// <param name="SetAside">Pending review approvals it set aside because their record no longer waits.</param>
public sealed record ApprovalReconciliation(int Queued, int SetAside)
{
    public int Total => Queued + SetAside;
}
