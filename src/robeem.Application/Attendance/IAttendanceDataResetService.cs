using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Deletes all of a tenant's attendance data so that it can be imported again: the punches as received and as normalized, the batches and
/// uploads, the calculated days, and the exceptions, corrections and approvals made on them. It keeps the configuration (integrations,
/// connections, devices, employee-id mappings, policies, rules, pay periods), the employees and the audit log. This is the second, and
/// widest, exception to the immutability of raw attendance (CLAUDE.md §1.2): it is allowed only where the host turns it on, to an
/// <c>IntegrationAdministrator</c> who types the tenant id, and it is audited in an entry that is kept.
/// </summary>
public interface IAttendanceDataResetService
{
    /// <summary>Counts what a reset would delete and what it would keep, for the current tenant.</summary>
    Task<AttendanceDataSummary> GetSummaryAsync(CancellationToken cancellationToken);

    /// <summary>Deletes all attendance data of the current tenant, when the host allows it and <paramref name="confirmation"/> is the tenant id.</summary>
    Task<AttendanceDataResetResult> ResetAsync(string confirmation, CancellationToken cancellationToken);
}
