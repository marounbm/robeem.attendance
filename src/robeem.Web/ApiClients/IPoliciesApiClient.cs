using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.ApiClients;

/// <summary>Attendance policies: the tenant-wide default and the optional policy of each time schedule.</summary>
public interface IPoliciesApiClient
{
    Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The settings of a policy, or null when it doesn't exist.</summary>
    Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
