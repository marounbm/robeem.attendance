namespace robeem.Application.Attendance;

/// <summary>What one <see cref="IAttendanceProcessingEngine.ProcessEmployeeDayAsync"/> call changed.</summary>
/// <param name="RecordsWritten">New records, or new versions of existing ones.</param>
/// <param name="RecordsUnchanged">Current records whose calculation did not change; kept as they were.</param>
/// <param name="RecordsRetired">Current records of shifts no longer scheduled that day; retired without a successor.</param>
/// <param name="ExceptionsRaised">New attendance exceptions, opened this call.</param>
/// <param name="ExceptionsResolved">Open exceptions whose problem is gone, closed this call.</param>
/// <param name="RecordsSentToReview">Unchanged records that were approved by the engine and now need a review (a stricter policy or a new exception), so they are no longer approved.</param>
public sealed record DayProcessingResult(int RecordsWritten, int RecordsUnchanged, int RecordsRetired, int ExceptionsRaised = 0, int ExceptionsResolved = 0, int RecordsSentToReview = 0)
{
    /// <summary>Gets a value indicating whether the call changed anything a person would notice: a record, an exception or a review status.</summary>
    public bool Changed => RecordsWritten + RecordsRetired + ExceptionsRaised + ExceptionsResolved + RecordsSentToReview > 0;
}
