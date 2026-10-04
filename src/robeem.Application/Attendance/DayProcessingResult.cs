namespace robeem.Application.Attendance;

/// <summary>What one <see cref="IAttendanceProcessingEngine.ProcessEmployeeDayAsync"/> call changed.</summary>
/// <param name="RecordsWritten">New records, or new versions of existing ones.</param>
/// <param name="RecordsUnchanged">Current records whose calculation did not change; kept as they were.</param>
/// <param name="RecordsRetired">Current records of shifts no longer scheduled that day; retired without a successor.</param>
/// <param name="ExceptionsRaised">New attendance exceptions, opened this call.</param>
/// <param name="ExceptionsResolved">Open exceptions whose problem is gone, closed this call.</param>
public sealed record DayProcessingResult(int RecordsWritten, int RecordsUnchanged, int RecordsRetired, int ExceptionsRaised = 0, int ExceptionsResolved = 0);
