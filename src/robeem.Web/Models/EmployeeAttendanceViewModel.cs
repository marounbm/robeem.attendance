using robeem.Contracts.Attendance;

namespace robeem.Web.Models;

/// <summary>One employee's attendance and exceptions over a date range: the "By employee" tab of the Attendance screen.</summary>
public sealed class EmployeeAttendanceViewModel
{
    /// <summary>Gets the most days one look covers, so the whole range is read at once (one day is one row, and a page holds at most 200).</summary>
    public const int MaxRangeDays = 92;

    /// <summary>Gets the days the range covers when none is asked for.</summary>
    public const int DefaultRangeDays = 30;

    /// <summary>Gets the employees the user can choose from.</summary>
    public IReadOnlyList<EmployeeOption> Employees { get; init; } = Array.Empty<EmployeeOption>();

    /// <summary>Gets the employee shown, or null while none is chosen.</summary>
    public int? EmployeeId { get; init; }

    public DateTime From { get; init; }

    public DateTime To { get; init; }

    /// <summary>Gets a value indicating whether the range asked for was longer than <see cref="MaxRangeDays"/> and was cut.</summary>
    public bool RangeShortened { get; init; }

    /// <summary>Gets the employee's days in the range, oldest first.</summary>
    public IReadOnlyList<RecordDayListItem> Days { get; init; } = Array.Empty<RecordDayListItem>();

    /// <summary>Gets the employee's exceptions on those days, closed ones included, oldest day first.</summary>
    public IReadOnlyList<ExceptionListItem> Exceptions { get; init; } = Array.Empty<ExceptionListItem>();

    public string? EmployeeName => Employees.FirstOrDefault(e => e.Id == EmployeeId)?.Name;

    public TimeSpan TotalWorked => Sum(d => d.TotalWorkingHours);

    public TimeSpan TotalOvertime => Sum(d => d.TotalOvertimeHours);

    public TimeSpan TotalBreak => Sum(d => d.TotalBreakHours);

    /// <summary>Gets the number of days that still have an exception that is not closed.</summary>
    public int DaysWithOpenExceptions => Days.Count(d => d.ExceptionCount > 0);

    /// <summary>Gets the number of exceptions that are not closed.</summary>
    public int OpenExceptions => Exceptions.Count(e => e.Status != "Closed");

    private TimeSpan Sum(Func<RecordDayListItem, TimeSpan> selector) => Days.Aggregate(TimeSpan.Zero, (total, day) => total + selector(day));
}
