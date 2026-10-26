using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Application.HR.Employees;

public interface IEmployeeService
{
    /// <summary>
    /// Lists one page of employees by last name, first name and number, with the number that match on every page together. The page
    /// size is 1 to 200 (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<EmployeeListItem>> ListAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Gets an employee as editable input, or <c>null</c> when it does not exist.</summary>
    Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>Lists the active employees who can be chosen as a manager, except <paramref name="excludeEmployeeId"/> (nobody manages themselves).</summary>
    Task<IReadOnlyList<ManagerOption>> ListManagerOptionsAsync(int? excludeEmployeeId, CancellationToken cancellationToken);

    Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken);

    Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken);
}
