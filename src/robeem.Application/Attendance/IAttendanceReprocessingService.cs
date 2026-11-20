namespace robeem.Application.Attendance;

/// <summary>
/// Calculates the attendance already held again from a date on, for when what the calculation reads has changed: the policy, the rules, a
/// holiday or a leave. Each day goes through <see cref="IAttendanceProcessingEngine"/>, which writes a new record version only when the
/// day comes out different, closes the open exceptions whose problem is gone and raises the ones a stricter policy or rule now finds; a day
/// that comes out the same is left as it was. Days in a closed pay period are never touched: payroll has them.
/// </summary>
public interface IAttendanceReprocessingService
{
    /// <summary>
    /// Calculates again the days from <paramref name="from"/> to today of one employee, or of everyone, that already have a record, a day summary
    /// or an exception (days nobody punched on are <see cref="IAttendanceLookbackService.CheckPeriodAsync"/>'s). Oldest day first; stops once
    /// <paramref name="maxDays"/> employee-days are done, always finishing the date it is on, and says where to carry on from.
    /// </summary>
    Task<ReprocessSinceResult> ReprocessSinceAsync(DateTime from, int? employeeId, int maxDays, CancellationToken cancellationToken);

    /// <summary>
    /// The same for the days from <paramref name="from"/> to <paramref name="to"/> (never later than today): what is done when a leave or a
    /// holiday is saved, so that the days it covers are settled at once.
    /// </summary>
    Task<ReprocessSinceResult> ReprocessRangeAsync(DateTime from, DateTime to, int? employeeId, int maxDays, CancellationToken cancellationToken);
}
