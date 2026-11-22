using robeem.DataModel;

namespace robeem.Tests.Attendance.Scenarios;

/// <summary>What a day must come to.</summary>
/// <param name="Exceptions">The kinds of exception that are open for the day.</param>
/// <param name="NeedsReview">Whether the day is left for a person (pending review) and not approved.</param>
/// <param name="HasRecord">Whether the day has a calculated record at all.</param>
/// <param name="WorkedMinutes">The minutes worked, when the day is complete enough to say; otherwise null.</param>
/// <param name="OvertimeMinutes">The minutes of overtime, when the day is complete enough to say; otherwise null.</param>
internal sealed record ExpectedDay(IReadOnlySet<AttendanceExceptionType> Exceptions, bool NeedsReview, bool HasRecord, int? WorkedMinutes, int? OvertimeMinutes);
