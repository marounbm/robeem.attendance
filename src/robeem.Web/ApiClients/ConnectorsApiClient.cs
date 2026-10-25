using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class ConnectorsApiClient : IConnectorsApiClient
{
    private const string Path = "api/attendance/connectors";

    private readonly HttpClient _http;

    public ConnectorsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<ConnectorListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<ConnectorListItem>>(response, cancellationToken);
    }

    public async Task<ConnectorInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<ConnectorInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(ConnectorInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<ConnectorInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, ConnectorInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<ConnectorInput>(response, id, cancellationToken);
    }
}
