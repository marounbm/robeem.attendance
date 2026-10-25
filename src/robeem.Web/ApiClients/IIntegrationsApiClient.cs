using robeem.Contracts.Attendance;

namespace robeem.Web.ApiClients;

/// <summary>robeem.Api's integrations (<c>api/attendance/integrations</c>).</summary>
public interface IIntegrationsApiClient
{
    Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets one integration, or null when it does not exist.</summary>
    Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets an integration as editable input, or null when it does not exist.</summary>
    Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectorOption>> ListConnectorsAsync(CancellationToken cancellationToken);

    /// <summary>The active custom file formats a manual-upload integration can be set to read its files by.</summary>
    Task<IReadOnlyList<PayloadFormatOption>> ListPayloadFormatOptionsAsync(CancellationToken cancellationToken);

    /// <summary>The active device types a device can be given.</summary>
    Task<IReadOnlyList<DeviceTypeOption>> ListDeviceTypeOptionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken);

    /// <summary>Gets the integration's health and KPIs, or null when it does not exist.</summary>
    Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Lists the devices of every integration, each with its integration and mapping count.</summary>
    Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Gets one device of an integration, or null when there is none.</summary>
    Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<ApiSaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken);

    Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the integration's raw punches, newest first, each beside the normalized punch it produced;
    /// null when the integration does not exist. Each call is audited by the Api.
    /// </summary>
    Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken);

    /// <summary>Describes how the integration's vendor maps raw data; null when the integration does not exist.</summary>
    Task<VendorMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Runs a sync now (or, with <paramref name="reconcile"/>, the wider reconciliation).</summary>
    Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken);

    /// <summary>Queues the punches that failed for want of a time zone to be normalized again; null when the integration does not exist.</summary>
    Task<RetryFailedResponse?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Queues the punches flagged for an unrecognized device to be normalized again; null when the integration does not exist.</summary>
    Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken);
}
