using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The payroll gate: the approved, exception-free days of a period, and nothing else about attendance.</summary>
public interface IPayrollApiClient
{
    /// <summary>
    /// All the approved days between two dates, both inclusive; for the CSV. A closed pay period yields none.
    /// <paramref name="employee"/> (a number or name) keeps only the matching employees. The Api refuses a request with
    /// neither an employee nor both dates; an employee with no dates reads every date of that employee.
    /// </summary>
    Task<IReadOnlyList<PayrollAttendanceDay>> GetApprovedAsync(DateTime? from, DateTime? to, string? employee, CancellationToken cancellationToken);

    /// <summary>One page of the same days for the screen, with the totals per employee over all of the matching days.</summary>
    Task<PayrollApprovedPage> GetApprovedPageAsync(DateTime? from, DateTime? to, string? employee, int page, int pageSize, CancellationToken cancellationToken);
}
