using System.Globalization;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class AuditApiClient : IAuditApiClient
{
    private readonly HttpClient _http;

    public AuditApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrWhiteSpace(action))
        {
            query.Add($"action={Uri.EscapeDataString(action.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query.Add($"entityType={Uri.EscapeDataString(entityType.Trim())}");
        }

        if (entityId is { } id)
        {
            query.Add($"entityId={id.ToString(CultureInfo.InvariantCulture)}");
        }

        using var response = await _http.GetAsync($"api/attendance/audit?{string.Join('&', query)}", cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<AuditLogEntry>>(response, cancellationToken);
    }
}
