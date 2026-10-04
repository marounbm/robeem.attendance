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
    Task<DayProcessingResult> ProcessEmployeeDayAsync(int employeeId, DateTime date, CancellationToken cancellationToken);
}
