using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Pages the days of <see cref="IPayrollAttendanceExtract"/> for a screen. Which days payroll may pay for is the
/// extract's decision alone (it needs each employee's policy, so it is made in memory over the range); this only
/// orders, totals and cuts a page, so a screen never receives or draws more than a page of days.
/// </summary>
public interface IPayrollApprovedPageService
{
    /// <summary>Gets one page of the approved days between two dates (both inclusive), with the totals per employee.</summary>
    /// <exception cref="ArgumentException">The end is before the start.</exception>
    Task<PayrollApprovedPage> GetPageAsync(DateTime from, DateTime to, int page, int pageSize, CancellationToken cancellationToken);
}
