using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>Gives the rules in force for a policy: the published set of its scope, else the policy's own settings written as rules.</summary>
public interface IRuleSetProvider
{
    /// <summary>The key of the tenant-wide scope in a dictionary: no schedule has id 0.</summary>
    public const int TenantScope = 0;

    Task<RuleSetSnapshot> ResolveAsync(AttendancePolicy policy, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the number of the published set in force for each scope: the schedule id, or <see cref="TenantScope"/> (0) for the tenant-wide one.
    /// A scope with no published set is not in the answer.
    /// </summary>
    Task<IReadOnlyDictionary<int, int>> GetPublishedVersionsAsync(CancellationToken cancellationToken);
}
