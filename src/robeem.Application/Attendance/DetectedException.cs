using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>An exception the detector found in one shift's punches, before it is matched against the stored ones.</summary>
/// <param name="Type">What kind of problem it is.</param>
/// <param name="MissingEventType">The absent punch, for a missing punch; otherwise null.</param>
/// <param name="NormalizedTransactionId">The out-of-order punch, for an invalid sequence; otherwise null.</param>
/// <param name="Description">What a reviewer reads.</param>
/// <param name="PunchType">For an invalid sequence, the type the out-of-order punch has (set by the engine); otherwise null.</param>
/// <param name="PunchAt">For an invalid sequence, when the out-of-order punch happened, as local wall-clock time (set by the engine); otherwise null.</param>
/// <param name="SuggestedTypes">For an invalid sequence, the types its out-of-order punch can be given so that this exception goes away (set by the engine, which can recalculate the shift); otherwise empty.</param>
internal sealed record DetectedException(
    AttendanceExceptionType Type,
    NormalizedEventType? MissingEventType,
    int? NormalizedTransactionId,
    string Description,
    IReadOnlyList<NormalizedEventType>? SuggestedTypes = null,
    NormalizedEventType? PunchType = null,
    DateTime? PunchAt = null);
