namespace robeem.Application.Attendance;

/// <summary>
/// Runs the processing engine for every employee day that has normalized punches the engine has not
/// seen yet, so attendance records appear without anybody asking for a day to be reprocessed.
/// Called by the background job after normalization.
/// </summary>
public interface IPendingDayProcessor
{
    /// <summary>
    /// Processes the days of the <c>Pending</c> normalized punches that name an employee, oldest first,
    /// at most <paramref name="maxDays"/> of them, and marks those punches <c>Processed</c>. A punch
    /// whose employee is not recognized stays <c>Pending</c> and is not touched. A day that fails is
    /// logged and left <c>Pending</c> for the next run; it does not stop the others. With room left, it
    /// also recalculates, once, the days of open out-of-order exceptions raised before the engine kept the
    /// punch's details and the types that would settle them, so the employee is offered the change.
    /// Returns the number of days processed.
    /// </summary>
    Task<int> ProcessPendingAsync(int maxDays, CancellationToken cancellationToken);
}
