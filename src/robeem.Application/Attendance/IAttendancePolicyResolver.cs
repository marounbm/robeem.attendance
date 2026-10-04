using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Finds the attendance policy that applies to an employee (CLAUDE.md §3). Normalization (P5) and
/// processing (P7) both resolve policies through this, so they always agree.
/// </summary>
public interface IAttendancePolicyResolver
{
    /// <summary>
    /// Returns, in order of precedence:
    /// <list type="number">
    /// <item>the policy of the employee's time schedule;</item>
    /// <item>the tenant-wide default (<c>TimeScheduleId</c> null), also used for an unknown
    /// employee (<paramref name="employeeId"/> null) or one with no schedule;</item>
    /// <item>when the tenant has no policy at all, an unsaved <see cref="AttendancePolicy"/>
    /// carrying the built-in defaults.</item>
    /// </list>
    /// The result is not tracked; do not modify it.
    /// </summary>
    Task<AttendancePolicy> ResolveAsync(int? employeeId, CancellationToken cancellationToken);
}
