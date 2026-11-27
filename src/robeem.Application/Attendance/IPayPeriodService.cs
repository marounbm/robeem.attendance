using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Pay periods: the dates payroll pays for, and whether each is closed. Closing a period ends its
/// extraction (the payroll gate returns none of its days); reopening needs a reason. Every change
/// is audited. A stand-in for a Payroll module that does not exist (CLAUDE.md §4).
/// </summary>
public interface IPayPeriodService
{
    /// <summary>Lists the company-wide pay periods, latest first. An employee's own periods (made when payroll closes them) are not listed here.</summary>
    Task<IReadOnlyList<PayPeriodItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes an employee's own pay period (the dates their pay cycle gives), making the row when there is none yet. Closed, payroll no longer
    /// reads that employee's days in it; other employees are not affected. Refused when the employee does not exist, the period is already
    /// closed, or it overlaps another period of the same employee (their cycle changed). Whether the period is ready to close is the
    /// caller's to check.
    /// </summary>
    Task<SaveResult> CloseForEmployeeAsync(int employeeId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken);

    /// <summary>Creates an open period. Refused when it ends before it starts, is longer than a year, or overlaps another period.</summary>
    Task<SaveResult> CreateAsync(PayPeriodInput input, CancellationToken cancellationToken);

    /// <summary>Closes an open period; closing a closed one is refused.</summary>
    Task<SaveResult> CloseAsync(int id, CancellationToken cancellationToken);

    /// <summary>Opens a closed period again, with a reason; reopening an open one is refused.</summary>
    Task<SaveResult> ReopenAsync(int id, string? reason, CancellationToken cancellationToken);
}
