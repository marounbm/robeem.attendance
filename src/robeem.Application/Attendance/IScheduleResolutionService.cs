namespace robeem.Application.Attendance;

/// <summary>
/// Answers "which shift should employee E work on date D" (CLAUDE.md §3.1) from the existing
/// roster entities: <c>TimeScheduleEmployee</c>, <c>TimeSchedule.GetCycleDay</c>,
/// <c>TimeScheduleLine</c> and <c>TimeScheduleShift</c>. Wherever the study compares punches with
/// "the employee's expected schedule", call this.
/// </summary>
public interface IScheduleResolutionService
{
    Task<ShiftResolution> ResolveShiftAsync(int employeeId, DateTime date, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves every date from <paramref name="fromDate"/> to <paramref name="toDate"/>, both
    /// included, in order. Loads the roster once, so prefer it to calling
    /// <see cref="ResolveShiftAsync"/> per day.
    /// </summary>
    Task<IReadOnlyList<ShiftResolution>> ResolveShiftsInRangeAsync(int employeeId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken);
}
