using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;

namespace robeem.Application.HR.Employees;

public interface IEmployeeService
{
    /// <summary>
    /// Lists one page of employees, with the number that match on every page together, sorted by <paramref name="sort"/> (the employee number, in natural
    /// order, to begin with) and ascending unless <paramref name="descending"/>. Ties break by the employee number, which is unique, so pages never overlap.
    /// The page size is 1 to 200 (default 20); a page past the end answers the last page.
    /// </summary>
    Task<PagedResult<EmployeeListItem>> ListAsync(string? search, bool includeInactive, int page, int pageSize, EmployeeSortField sort, bool descending, CancellationToken cancellationToken);

    /// <summary>Gets an employee as editable input, or <c>null</c> when it does not exist.</summary>
    Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>Lists the active employees who can be chosen as a manager, except <paramref name="excludeEmployeeId"/> (nobody manages themselves).</summary>
    Task<IReadOnlyList<ManagerOption>> ListManagerOptionsAsync(int? excludeEmployeeId, CancellationToken cancellationToken);

    /// <summary>Finds employees by part of the number or name, ignoring case, for a search box: at most 20, active ones first. An empty search finds none.</summary>
    Task<IReadOnlyList<EmployeeLookupItem>> LookupAsync(string? search, CancellationToken cancellationToken);

    Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken);

    Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken);
}
