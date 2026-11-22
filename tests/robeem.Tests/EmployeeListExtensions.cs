using robeem.Application.HR.Employees;
using robeem.Contracts.Common;
using robeem.Contracts.HR.Employees;
using robeem.Web.ApiClients;

namespace robeem.Tests;

/// <summary>The employees list as the tests mostly ask for it: in the order it starts in, by employee number, ascending.</summary>
internal static class EmployeeListExtensions
{
    public static Task<PagedResult<EmployeeListItem>> ListAsync(this IEmployeeService service, string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken) =>
        service.ListAsync(search, includeInactive, page, pageSize, EmployeeSortField.Number, false, cancellationToken);

    public static Task<PagedResult<EmployeeListItem>> ListAsync(this IEmployeesApiClient client, string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken) =>
        client.ListAsync(search, includeInactive, page, pageSize, EmployeeSortField.Number, false, cancellationToken);
}
