namespace robeem.Application.HR.Absence;

/// <summary>
/// Where leaves and public holidays are read from. HR owns the data; Attendance reads it through this
/// interface (HR never depends on Attendance).
/// </summary>
public interface IEmployeeAbsenceCalendar
{
    /// <summary>
    /// Gets the excused time that overlaps <paramref name="from"/> to <paramref name="to"/> (both inclusive dates)
    /// for the given employees: whole days one entry per date, part-day leaves with their times.
    /// </summary>
    Task<AbsenceCalendar> GetAsync(IReadOnlyCollection<int> employeeIds, DateTime from, DateTime to, CancellationToken cancellationToken);
}
