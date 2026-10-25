using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class ConnectionsApiClient : IConnectionsApiClient
{
    private readonly HttpClient _http;

    public ConnectionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ConnectionListItem>?> ListAsync(int integrationId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path(integrationId), cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<List<ConnectionListItem>>(response, cancellationToken);
    }

    public async Task<ConnectionInput?> GetInputAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path(integrationId)}/{Number(connectionId)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<ConnectionInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(int integrationId, ConnectionInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path(integrationId), input, cancellationToken);
        return await ApiResponses.SaveAsync<ConnectionInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int integrationId, int connectionId, ConnectionInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path(integrationId)}/{Number(connectionId)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<ConnectionInput>(response, connectionId, cancellationToken);
    }

    public async Task<ApiSaveResult> MakeDefaultAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{Path(integrationId)}/{Number(connectionId)}/make-default", null, cancellationToken);
        return await ApiResponses.SaveAsync<ConnectionInput>(response, connectionId, cancellationToken);
    }

    public async Task<ConnectionTestResult?> TestAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{Path(integrationId)}/{Number(connectionId)}/test", null, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<ConnectionTestResult>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TransportDescriptorItem>> ListDescriptorsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/attendance/transports/descriptors", cancellationToken);
        return await ApiResponses.ReadAsync<List<TransportDescriptorItem>>(response, cancellationToken);
    }

    private static string Path(int integrationId) => $"api/attendance/integrations/{Number(integrationId)}/connections";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
