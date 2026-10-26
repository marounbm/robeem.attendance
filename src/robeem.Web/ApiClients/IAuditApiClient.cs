using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

/// <summary>The attendance audit trail: what was decided, changed or read, by whom, newest first.</summary>
public interface IAuditApiClient
{
    /// <summary>Lists one page of entries, newest first; every filter is optional. A page past the end answers the last page.</summary>
    Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken);
}
