using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

public interface IIntegrationHealthService
{
    /// <summary>
    /// The composite health of an integration: its stored <c>HealthStatus</c> and breaker, plus the
    /// KPIs over the last <see cref="AttendanceSyncOptions.HealthWindowHours"/> hours. Null when the
    /// integration does not exist. Computed from the existing tables; the only write is the audit entry for reading them (CLAUDE.md §2.8).
    /// </summary>
    Task<IntegrationHealthReport?> GetIntegrationHealthAsync(int integrationId, CancellationToken cancellationToken);
}
