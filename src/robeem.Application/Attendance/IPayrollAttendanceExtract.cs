using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// The only sanctioned read path into attendance data for anything outside this feature, payroll
/// above all (CLAUDE.md §1.8, study §18.3). Payroll code must not query raw or normalized
/// attendance. The tenant is the current one; there is no tenant parameter.
/// </summary>
public interface IPayrollAttendanceExtract
{
    /// <summary>
    /// Returns the days in [<paramref name="payPeriodStart"/>, <paramref name="payPeriodEnd"/>] (dates
    /// only; both inclusive) that payroll may pay for, ordered by date and employee number. A day
    /// is returned only when <b>all</b> of this holds:
    /// <list type="bullet">
    /// <item>its summary and every current record of it are Approved;</item>
    /// <item>no exception of that employee and date is still Open or PendingCorrection;</item>
    /// <item>a record with overtime under a policy that requires pre-approval has an Approved or
    /// Applied correction of that employee and date;</item>
    /// <item>no closed <see cref="robeem.DataModel.PayPeriod"/> contains the date.</item>
    /// </list>
    /// A day that fails any of this is left out whole, never partly paid.
    /// </summary>
    /// <exception cref="ArgumentException">The end is before the start.</exception>
    Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedRecordsAsync(DateTime payPeriodStart, DateTime payPeriodEnd, CancellationToken cancellationToken);
}
