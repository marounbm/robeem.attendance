namespace robeem.Application.Attendance;

/// <summary>
/// Turns an employee's normalized punches into attendance records and a day summary (study §8,
/// §17.1), comparing them with the expected shifts from <see cref="IScheduleResolutionService"/>
/// under the policy from <see cref="IAttendancePolicyResolver"/>.
/// </summary>
public interface IAttendanceProcessingEngine
{
    /// <summary>
    /// Calculates every shift that starts on <paramref name="date"/>, plus any work that day outside
    /// a shift, and refreshes that date's <c>AttendanceDaySummary</c>. Safe to repeat: a record
    /// whose calculation is unchanged is kept as it is (with its review status); a changed one gets
    /// a new version, and the previous version is retired, never rewritten.
    /// </summary>
    /// <remarks>
    /// It also raises an <c>AttendanceException</c> for each missing punch and invalid sequence it
    /// finds (study §9, §14.1), once, however often the day is processed. A shift with an exception
    /// that is not closed is never <c>Approved</c>. An open exception whose problem has gone, for
    /// instance because the punch arrived late, is closed as auto-resolved.
    /// </remarks>
    Task<DayProcessingResult> ProcessEmployeeDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken);
}
