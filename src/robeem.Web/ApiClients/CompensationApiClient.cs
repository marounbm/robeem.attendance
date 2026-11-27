using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;

namespace robeem.Web.ApiClients;

public sealed class CompensationApiClient : ICompensationApiClient
{
    private const string Path = "api/attendance/payroll/compensation";

    private readonly HttpClient _http;

    public CompensationApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<CompensationOverviewItem>> ListAsync(string? search, string mode, DateTime? date, int page, int pageSize, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}?{Query(search, mode, date)}&page={Number(page)}&pageSize={Number(pageSize)}", cancellationToken);
        return await ApiResponses.ReadAsync<PagedResult<CompensationOverviewItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<CompensationOverviewItem>> ListAllAsync(string? search, string mode, DateTime? date, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/all?{Query(search, mode, date)}", cancellationToken);
        return await ApiResponses.ReadAsync<List<CompensationOverviewItem>>(response, cancellationToken);
    }

    public async Task<CompensationReport?> GetReportAsync(int employeeId, string mode, DateTime? date, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{Number(employeeId)}?{Query(null, mode, date)}", cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<CompensationReport>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> DecideAsync(CompensationDecisionRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/decisions", request, cancellationToken);
        return await ApiResponses.SaveAsync<CompensationDecisionRequest>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> CloseAsync(CompensationCloseRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/close", request, cancellationToken);
        return await ApiResponses.SaveAsync<CompensationCloseRequest>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> ReopenAsync(CompensationReopenRequest request, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{Path}/reopen", request, cancellationToken);
        return await ApiResponses.SaveAsync<CompensationReopenRequest>(response, null, cancellationToken);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Query(string? search, string mode, DateTime? date)
    {
        var parts = new List<string> { $"mode={Uri.EscapeDataString(mode)}" };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (date is { } day)
        {
            parts.Add($"date={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        }

        return string.Join("&", parts);
    }
}
