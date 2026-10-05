using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;

namespace robeem.Application.Attendance;

/// <summary>
/// Management of attendance policies: one tenant-wide default and at most one policy per time
/// schedule. A policy applies to days processed after the change; days already processed keep their
/// result until they are reprocessed. Every change is audited.
/// </summary>
public interface IAttendancePolicyService
{
    /// <summary>Lists the policies: the tenant default first, then those of schedules by schedule name.</summary>
    Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken);

    Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    /// <summary>The time schedules a policy can be given to, by name.</summary>
    Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken);

    /// <summary>Creates the tenant default (no schedule) or a schedule's policy; a second one for the same scope is refused.</summary>
    Task<SaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken);

    /// <summary>Changes the settings of a policy. Its scope (default, or a schedule) can't change.</summary>
    Task<SaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a schedule's policy, which then falls back to the default. The tenant default can't be deleted.</summary>
    Task<SaveResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
