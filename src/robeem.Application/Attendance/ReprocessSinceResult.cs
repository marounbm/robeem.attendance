namespace robeem.Application.Attendance;

/// <summary>What calculating attendance again from a date did.</summary>
/// <param name="From">The first day looked at (the date asked for, or the start date when that is later).</param>
/// <param name="To">The last day looked at: today.</param>
/// <param name="DaysExamined">Employee-days calculated again.</param>
/// <param name="DaysChanged">Of those, the ones whose records, or exceptions, changed.</param>
/// <param name="RecordsWritten">New record versions.</param>
/// <param name="RecordsRetired">Records retired with no successor.</param>
/// <param name="ExceptionsRaised">Exceptions opened.</param>
/// <param name="ExceptionsResolved">Open exceptions closed because their problem is gone.</param>
/// <param name="RecordsSentToReview">Unchanged records the engine had approved that now need a review.</param>
/// <param name="DaysFailed">Employee-days that could not be calculated; asking again tries them again.</param>
/// <param name="DaysInClosedPeriods">Employee-days left alone because their pay period is closed.</param>
/// <param name="NextFrom">The date to start from to carry on, when the run stopped at its limit; null when it did everything.</param>
public sealed record ReprocessSinceResult(
    DateTime From,
    DateTime To,
    int DaysExamined,
    int DaysChanged,
    int RecordsWritten,
    int RecordsRetired,
    int RecordsSentToReview,
    int ExceptionsRaised,
    int ExceptionsResolved,
    int DaysFailed,
    int DaysInClosedPeriods,
    DateTime? NextFrom);
