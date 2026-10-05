using robeem.Application.Common;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

/// <summary>
/// Setting up the tenant's connections to attendance providers: integrations, their devices, and
/// the mapping of the vendor's employee ids to employees. An integration is paused, never deleted:
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

    Task<SaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken);

    Task<SaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken);

    /// <summary>Deletes a device that no punch refers to; one with punches on record is refused (raw data is never touched).</summary>
    Task<SaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken);

    /// <summary>Lists an integration's vendor-id mappings; null when the integration does not exist.</summary>
    Task<IReadOnlyList<MappingItem>?> ListMappingsAsync(int integrationId, CancellationToken cancellationToken);

    Task<SaveResult> AddMappingAsync(int integrationId, MappingInput input, CancellationToken cancellationToken);

    Task<SaveResult> DeleteMappingAsync(int integrationId, int mappingId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a sync, or a reconciliation, now instead of at the integration's next scheduled time.
    /// Audited. Null when the integration does not exist; a provider failure is the outcome, not an error.
    /// </summary>
    Task<SyncOutcomeResponse?> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken);
}
