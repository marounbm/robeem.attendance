using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class VendorsApiClient : IVendorsApiClient
{
    private const string Path = "api/attendance/vendors";

    private readonly HttpClient _http;

    public VendorsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<VendorListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<VendorListItem>>(response, cancellationToken);
    }

    public async Task<VendorInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}/input", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<VendorInput>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(VendorInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<VendorInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, VendorInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<VendorInput>(response, id, cancellationToken);
    }
}
