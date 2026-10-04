using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>An exception the detector found in one shift's punches, before it is matched against the stored ones.</summary>
/// <param name="Type">What kind of problem it is.</param>
/// <param name="MissingEventType">The absent punch, for a missing punch; otherwise null.</param>
/// <param name="NormalizedTransactionId">The out-of-order punch, for an invalid sequence; otherwise null.</param>
/// <param name="Description">What a reviewer reads.</param>
internal sealed record DetectedException(AttendanceExceptionType Type, NormalizedEventType? MissingEventType, int? NormalizedTransactionId, string Description);
