using System.Globalization;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.Web.Models;

namespace robeem.Web.ApiClients;

public sealed class AuditApiClient : IAuditApiClient
{
    private readonly HttpClient _http;

    public AuditApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<AuditLogEntry>> ListAsync(string? action, string? entityType, int? entityId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken)
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

        if (from is { } start)
        {
            query.Add($"from={start.ToString(AuditIndexViewModel.MinuteFormat, CultureInfo.InvariantCulture)}");
        }

        if (to is { } end)
        {
            query.Add($"to={end.ToString(AuditIndexViewModel.MinuteFormat, CultureInfo.InvariantCulture)}");
        }

        using var response = await _http.GetAsync($"api/attendance/audit?{string.Join('&', query)}", cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<AuditLogEntry>>(response, cancellationToken);
    }
}
