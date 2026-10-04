using robeem.Contracts.HR.Employees;

namespace robeem.Application.HR.Employees;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeListItem>> ListAsync(string? search, bool includeInactive, CancellationToken cancellationToken);

    /// <summary>Gets an employee as editable input, or <c>null</c> when it does not exist.</summary>
    Task<EmployeeInput?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleOption>> ListSchedulesAsync(CancellationToken cancellationToken);

    Task<EmployeeSaveResult> CreateAsync(EmployeeInput input, CancellationToken cancellationToken);

    Task<EmployeeSaveResult> UpdateAsync(int id, EmployeeInput input, CancellationToken cancellationToken);
}
