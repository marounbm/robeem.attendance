using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Correcting a whole day. The employee sees every punch of the day and sends, with one reason, the edits that put it right: a punch given
/// another type, a missing punch added, a punch that should not count ignored. The approvers decide the request as a whole, as for any
/// correction, and an approved request is folded into the day by the processing engine. Corrections are additive (CLAUDE.md §1.7): no
/// punch row is changed. Only the employee the day belongs to can see or correct it here.
/// </summary>
public interface IAttendanceDayCorrectionService
{
    /// <summary>
    /// The day's shifts, every punch with what can be done to it, and the exceptions to settle. Reading the punches is a direct read of
    /// normalized attendance, so it is audited. Null when the employee does not exist or is not the viewer.
    /// </summary>
    Task<DayCorrectionView?> GetDayAsync(int employeeId, DateTime date, int viewerEmployeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the edits for approval. The day needs an open exception and no correction already waiting. Every edit is checked: the punch
    /// must be one of the day's that counts, a type must be a real one, an added punch must be on the day (or the day after, for a shift that ends
    /// after midnight) and not in the future. The day's open exceptions then wait for the decision. Nothing is saved when any edit is refused.
    /// </summary>
    Task<SaveResult> ProposeAsync(int employeeId, DayCorrectionRequest request, int requestedByEmployeeId, CancellationToken cancellationToken);
}
