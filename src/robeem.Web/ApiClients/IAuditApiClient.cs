using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>The attendance audit trail: what was decided, changed or read, by whom, newest first.</summary>
public interface IAuditApiClient
{
    /// <summary>Lists entries, newest first; every filter is optional.</summary>
    Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken);
}
