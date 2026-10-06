namespace robeem.Application.Common;

/// <summary>
/// Where a service outside Attendance records that someone changed something that attendance
/// depends on (a time schedule, for one), so the change shows in the audit log. HR code uses this
/// instead of the attendance audit trail, because HR must not depend on Attendance
/// (CLAUDE.md §4); the Attendance folder supplies the implementation.
/// </summary>
public interface IChangeAuditTrail
{
    /// <summary>Stages an entry for the current user in the caller's unit of work; the caller saves, so it commits or rolls back with the change.</summary>
    void Record(string action, string entityType, int? entityId, string? details = null);
}
