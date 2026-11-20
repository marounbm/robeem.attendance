namespace robeem.Application.HR.Absence;

/// <summary>
/// Told by HR when a leave or a holiday is saved, changed or deleted, so that whoever works from the excused time can settle the days it
/// covers at once. HR owns this and knows nothing of who listens (HR does not depend on Attendance).
/// </summary>
public interface IAbsenceChangeNotifier
{
    /// <summary>The excused time changed on the days from <paramref name="from"/> to <paramref name="to"/>, inclusive, for one employee or (null) for everyone.</summary>
    Task AbsenceChangedAsync(int? employeeId, DateTime from, DateTime to, CancellationToken cancellationToken);
}
