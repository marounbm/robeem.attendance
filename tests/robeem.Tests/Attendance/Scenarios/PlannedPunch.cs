using robeem.DataModel;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>One punch of a plan, with the type the clock puts on it (<see cref="NormalizedEventType.Unknown"/> for none) and the clock's local time.</summary>
internal sealed record PlannedPunch(NormalizedEventType Type, DateTime At);
