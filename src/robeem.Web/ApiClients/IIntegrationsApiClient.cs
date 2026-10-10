using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's integrations (<c>api/attendance/integrations</c>) and the CSV upload.</summary>
public interface IIntegrationsApiClient
{
    Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets one integration, or null when it does not exist.</summary>
    Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets an integration as editable input, or null when it does not exist.</summary>
    Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken);

    /// <summary>Gets the integration's health and KPIs, or null when it does not exist.</summary>
    Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddMappingAsync(int integrationId, MappingInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int mappingId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the integration's raw punches, newest first, each beside the normalized punch it produced;
    /// null when the integration does not exist. Each call is audited by the Api.
    /// </summary>
    Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken);

    /// <summary>Describes how the integration's provider maps raw data; null when the integration does not exist.</summary>
    Task<ProviderMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Runs a sync now (or, with <paramref name="reconcile"/>, the wider reconciliation).</summary>
    Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken);

    /// <summary>Uploads an attendance file into the integration's raw transactions.</summary>
    Task<ApiCommandResult<AttendanceIngestionResult>> UploadAsync(int integrationId, Stream file, string fileName, CancellationToken cancellationToken);
}
