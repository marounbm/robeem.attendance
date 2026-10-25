using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

public sealed class IntegrationsApiClient : IIntegrationsApiClient
{
    private const string Path = "api/attendance/integrations";

    private readonly HttpClient _http;

    public IntegrationsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(Path, cancellationToken);
        return await ApiResponses.ReadAsync<List<IntegrationListItem>>(response, cancellationToken);
    }

    public Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) =>
        GetOrNullAsync<IntegrationListItem>(IntegrationPath(id), cancellationToken);

    public Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken) =>
        GetOrNullAsync<IntegrationInput>($"{IntegrationPath(id)}/input", cancellationToken);

    public Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken) =>
        GetOrNullAsync<IntegrationHealthReport>($"{IntegrationPath(id)}/health", cancellationToken);

    public async Task<IReadOnlyList<ConnectorOption>> ListConnectorsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/connectors", cancellationToken);
        return await ApiResponses.ReadAsync<List<ConnectorOption>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<PayloadFormatOption>> ListPayloadFormatOptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/attendance/payload-formats/options", cancellationToken);
        return await ApiResponses.ReadAsync<List<PayloadFormatOption>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceTypeOption>> ListDeviceTypeOptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("api/attendance/device-types/options", cancellationToken);
        return await ApiResponses.ReadAsync<List<DeviceTypeOption>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/employee-options", cancellationToken);
        return await ApiResponses.ReadAsync<List<EmployeeOption>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(Path, input, cancellationToken);
        return await ApiResponses.SaveAsync<IntegrationInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync(IntegrationPath(id), input, cancellationToken);
        return await ApiResponses.SaveAsync<IntegrationInput>(response, id, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{IntegrationPath(integrationId)}/devices", cancellationToken);
        return await ApiResponses.ReadAsync<List<DeviceItem>>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{Path}/devices", cancellationToken);
        return await ApiResponses.ReadAsync<List<DeviceItem>>(response, cancellationToken);
    }

    public Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) =>
        GetOrNullAsync<DeviceItem>($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}", cancellationToken);

    public async Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{IntegrationPath(integrationId)}/devices", input, cancellationToken);
        return await ApiResponses.SaveAsync<DeviceInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PutAsJsonAsync($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}", input, cancellationToken);
        return await ApiResponses.SaveAsync<DeviceInput>(response, deviceId, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}", cancellationToken);
        return await ApiResponses.SaveAsync<DeviceInput>(response, deviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}/mappings", cancellationToken);
        return await ApiResponses.ReadAsync<List<MappingItem>>(response, cancellationToken);
    }

    public async Task<ApiSaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}/mappings", input, cancellationToken);
        return await ApiResponses.SaveAsync<MappingInput>(response, null, cancellationToken);
    }

    public async Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync($"{IntegrationPath(integrationId)}/devices/{Number(deviceId)}/mappings/{Number(mappingId)}", cancellationToken);
        return await ApiResponses.SaveAsync<MappingInput>(response, mappingId, cancellationToken);
    }

    public async Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken)
    {
        var query = new List<string> { $"take={Number(take)}" };
        if (batchId is { } batch)
        {
            query.Add($"batchId={Number(batch)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status.Trim())}");
        }

        return await GetOrNullAsync<List<RawNormalizedMappingItem>>($"{IntegrationPath(integrationId)}/raw-mapping?{string.Join('&', query)}", cancellationToken);
    }

    public Task<VendorMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken) =>
        GetOrNullAsync<VendorMappingGuide>($"{IntegrationPath(integrationId)}/raw-mapping/guide", cancellationToken);

    public async Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{IntegrationPath(integrationId)}/{(reconcile ? "reconcile" : "sync")}", null, cancellationToken);
        return await ApiResponses.ReadAsync<SyncOutcomeResponse>(response, cancellationToken);
    }

    public async Task<RetryFailedResponse?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{IntegrationPath(integrationId)}/retry-failed", null, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<RetryFailedResponse>(response, cancellationToken);
    }

    public async Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync($"{IntegrationPath(integrationId)}/renormalize-flagged", null, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<RenormalizeFlaggedResponse>(response, cancellationToken);
    }

    private static string IntegrationPath(int id) => $"{Path}/{Number(id)}";

    private static string Number(int id) => id.ToString(CultureInfo.InvariantCulture);

    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound ? null : await ApiResponses.ReadAsync<T>(response, cancellationToken);
    }
}
