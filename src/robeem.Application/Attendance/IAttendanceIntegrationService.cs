using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

/// <summary>
/// Setting up the tenant's connections to attendance providers: integrations, their devices, and
/// the mapping of the vendor's employee ids to employees, which is kept per device. An integration is paused, never deleted:
/// raw transactions point at it (CLAUDE.md §1.2). The connection secret is only ever a reference
/// (<c>ConnectionConfigRef</c>); the secret itself lives in configuration.
/// </summary>
public interface IAttendanceIntegrationService
{
    Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken);

    Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken);

    /// <summary>Gets an integration as editable input, or null when it does not exist.</summary>
    Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken);

    /// <summary>Lists the providers of the catalog, active ones first. Only an active provider can be chosen for a new integration.</summary>
    Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken);

    /// <summary>Lists the active employees, for choosing whom a vendor's employee id stands for.</summary>
    Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken);

    Task<SaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken);

    /// <summary>Updates an integration. Its provider can't change: its devices and raw transactions belong to that adapter.</summary>
    Task<SaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken);

    /// <summary>Lists an integration's devices; null when the integration does not exist.</summary>
    Task<IReadOnlyList<DeviceItem>?> ListDevicesAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>Lists the devices of every integration, by integration and name, each with its mapping count.</summary>
    Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Gets one device of an integration; null when there is none.</summary>
    Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<SaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a device that no punch and no mapping refers to; one with punches on record is refused (raw data is never touched).</summary>
    Task<SaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    /// <summary>Lists a device's vendor-id mappings; null when the device does not exist in the integration.</summary>
    Task<IReadOnlyList<MappingItem>?> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    Task<SaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken);

    Task<SaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the integration's raw punches, newest first, each with the normalized punch it produced
    /// (empty for a row that is waiting, failed or was skipped). Read only. Audited, because it is a
    /// direct read of raw and normalized attendance (CLAUDE.md §2.8). Null when the integration does not exist.
    /// </summary>
    Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawToNormalizedAsync(int integrationId, RawNormalizedFilter filter, CancellationToken cancellationToken);

    /// <summary>Describes how the integration's provider maps raw data. Null when the integration does not exist.</summary>
    Task<ProviderMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a sync, or a reconciliation, now instead of at the integration's next scheduled time.
    /// Audited. Null when the integration does not exist; a provider failure is the outcome, not an error.
    /// </summary>
    Task<SyncOutcomeResponse?> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the integration's raw punches that failed for want of a time zone back to Pending, so the
    /// normalization job reads them again (after the integration's time zone, or a device, was set).
    /// Returns how many were queued; null when the integration does not exist. Audited.
    /// </summary>
    Task<int?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the integration's punches that were normalized with the "unrecognized device" flag out of the
    /// calculation (status Excluded: the punch itself is never edited) and queues their raw rows to be normalized again,
    /// once their device can be resolved (the integration's device for uploaded files, or a device registered since).
    /// Says how many were queued and how many could not be resolved yet; null when the integration does not exist. Audited.
    /// </summary>
    Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken);
}
