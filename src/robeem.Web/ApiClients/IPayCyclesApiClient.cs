using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's pay cycles (<c>api/attendance/pay-cycles</c>): how often the tenant, and each employee, is paid.</summary>
public interface IPayCyclesApiClient
{
    /// <summary>The default cycle as it applies, with the periods around today.</summary>
    Task<PayCycleItem> GetDefaultAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveDefaultAsync(PayCycleInput input, CancellationToken cancellationToken);

    /// <summary>One page of the active employees with the cycle that applies to each.</summary>
    Task<PagedResult<EmployeePayCycleItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>An employee's own cycle; null when they have none.</summary>
    Task<PayCycleInput?> GetEmployeeAsync(int employeeId, CancellationToken cancellationToken);

    Task<ApiSaveResult> SaveEmployeeAsync(int employeeId, PayCycleInput input, CancellationToken cancellationToken);

    /// <summary>Takes away an employee's own cycle, so the default applies to them again.</summary>
    Task<ApiSaveResult> RemoveEmployeeAsync(int employeeId, CancellationToken cancellationToken);
}
