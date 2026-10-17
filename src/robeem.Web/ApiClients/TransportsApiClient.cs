using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class TransportsApiClient : ITransportsApiClient
{
    private const string Path = "api/attendance/transports";

    private readonly HttpClient _http;

    public TransportsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<TransportItem>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<TransportInput>(response, id, cancellationToken);
    }
}
