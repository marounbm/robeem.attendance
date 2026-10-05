using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;
using robeem.Contracts.HR.Employees;

namespace robeem.Web.ApiClients;

public sealed class PoliciesApiClient : IPoliciesApiClient
{
    private const string Path = "api/attendance/policies";

    private readonly HttpClient _http;

    public PoliciesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<PolicyListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<PolicyListItem>>(response, cancellationToken);
    }

    public async Task<PolicyInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{PolicyPath(id)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<PolicyInput>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleOption>> ListScheduleOptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/schedule-options", cancellationToken);
        return await ApiResponses.ReadAsync<List<ScheduleOption>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(PolicyInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<PolicyInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, PolicyInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(PolicyPath(id), input, cancellationToken);
        return await ApiResponses.SaveAsync<PolicyInput>(response, id, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(PolicyPath(id), cancellationToken);
        return await ApiResponses.SaveAsync<PolicyInput>(response, id, cancellationToken);
    }

    private static string PolicyPath(int id) => $"{Path}/{id.ToString(CultureInfo.InvariantCulture)}";
}
