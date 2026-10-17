using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class ProvidersApiClient : IProvidersApiClient
{
    private const string Path = "api/attendance/providers";

    private readonly HttpClient _http;

    public ProvidersApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ProviderListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<ProviderListItem>>(response, cancellationToken);
    }

    public async Task<ProviderInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<ProviderInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(ProviderInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<ProviderInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, ProviderInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<ProviderInput>(response, id, cancellationToken);
    }
}
