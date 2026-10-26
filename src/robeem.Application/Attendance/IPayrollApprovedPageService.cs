using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Pages the days of <see cref="IPayrollAttendanceExtract"/> for a screen. Which days payroll may pay for is the
/// extract's decision alone (it needs each employee's policy, so it is made in memory over the range); this only
/// orders, totals and cuts a page, so a screen never receives or draws more than a page of days.
/// A query needs an employee, a period (both dates) or both: payroll never reads everything
/// (<see cref="PayrollApprovedPageService.Validate"/>).
/// </summary>
public interface IPayrollApprovedPageService
{
    /// <summary>
    /// Gets one page of the approved days between two dates (both inclusive), with the totals per employee. <paramref name="employee"/>
    /// narrows both to the employees whose number or name contains it; the totals follow the filter, not the page. With an
    /// employee and no dates, every date of that employee is read.
    /// </summary>
    /// <exception cref="ArgumentException">Neither an employee nor a period was given, only one date was given, or the end is before the start.</exception>
    Task<PayrollApprovedPage> GetPageAsync(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Gets every day of the same query, not one page: for the CSV.</summary>
    /// <exception cref="ArgumentException">As <see cref="GetPageAsync"/>.</exception>
    Task<IReadOnlyList<PayrollAttendanceDay>> GetDaysAsync(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken);
}
