using System.Globalization;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class AuditApiClient : IAuditApiClient
{
    private readonly HttpClient _http;

    public AuditApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, int take, CancellationToken cancellationToken)
    {
        var query = new List<string>();
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

        query.Add($"take={take.ToString(CultureInfo.InvariantCulture)}");

        using var response = await _http.GetAsync($"api/attendance/audit?{string.Join('&', query)}", cancellationToken);
        return await ApiResponses.ReadAsync<List<AuditLogEntry>>(response, cancellationToken);
    }
}
