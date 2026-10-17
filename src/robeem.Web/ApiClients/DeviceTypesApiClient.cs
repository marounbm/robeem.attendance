using System.Globalization;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class DeviceTypesApiClient : IDeviceTypesApiClient
{
    private const string Path = "api/attendance/device-types";

    private readonly HttpClient _http;

    public DeviceTypesApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<DeviceTypeItem>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<DeviceTypeInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{Path}/{id.ToString(CultureInfo.InvariantCulture)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<DeviceTypeInput>(response, id, cancellationToken);
    }
}
