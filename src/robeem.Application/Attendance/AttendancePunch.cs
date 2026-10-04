using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>One punch as the processing engine sees it.</summary>
/// <param name="NormalizedTransactionId">The normalized row it came from.</param>
/// <param name="Type">Its event type; for an inferred punch, the type processing assigned.</param>
/// <param name="Utc">The instant; elapsed times are measured on it.</param>
/// <param name="Local">The local wall-clock time; compared with the schedule.</param>
/// <param name="Source">Whether the type came from the vendor or was inferred.</param>
internal sealed record AttendancePunch(int NormalizedTransactionId, NormalizedEventType Type, DateTimeOffset Utc, DateTime Local, AttendancePunchSource Source);
