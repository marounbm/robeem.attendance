using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Application.Attendance;

/// <summary>
/// How often each employee is paid: the tenant's default cycle and an optional cycle of an employee's own. The pay periods follow from
/// the cycles (<see cref="PayCycleCalendar"/>); they are never stored until payroll closes one.
/// </summary>
public interface IPayCycleService
{
    /// <summary>Gets the tenant's default cycle; null when none is set (the calendar month applies).</summary>
    Task<PayCycleInput?> GetDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Gets the default as it applies, with the periods around today (the calendar month when none is set).</summary>
    Task<PayCycleItem> DescribeDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Sets the tenant's default cycle.</summary>
    Task<SaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Lists one page of the employees (active ones, by employee number) with the cycle that applies to each. The page size is 1 to 200
    /// (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<EmployeePayCycleItem>> ListEmployeesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Gets an employee's own cycle; null when they have none (or no such employee).</summary>
    Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>Gives an employee a cycle of their own.</summary>
    Task<SaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken);

    /// <summary>Takes away an employee's own cycle, so the default applies to them again.</summary>
    Task<SaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken);
}
