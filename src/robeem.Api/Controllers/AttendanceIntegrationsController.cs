using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using robeem.Api.Security;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Api.Controllers;

/// <summary>
/// Integrations: the tenant's connections to attendance providers, their devices and each device's
/// vendor-id mappings, run-now, and health. An integration is paused, never deleted (raw data points at it).
/// </summary>
[ApiController]
[Authorize(Roles = AttendanceRoles.IntegrationAdministrator)]
[Route("api/attendance/integrations")]
public sealed class AttendanceIntegrationsController : ControllerBase
{
    private readonly IAttendanceIntegrationService _integrations;
    private readonly IIntegrationHealthService _health;

    public AttendanceIntegrationsController(IAttendanceIntegrationService integrations, IIntegrationHealthService health)
    {
        _integrations = integrations;
        _health = health;
    }

    [HttpGet]
    public async Task<IReadOnlyList<IntegrationListItem>> List(CancellationToken cancellationToken) =>
        await _integrations.ListAsync(cancellationToken);

    /// <summary>The providers of the catalog; only an active one can be chosen for a new integration.</summary>
    [HttpGet("providers")]
    public async Task<IReadOnlyList<ProviderOption>> Providers(CancellationToken cancellationToken) =>
        await _integrations.ListProvidersAsync(cancellationToken);

    /// <summary>The active employees, to choose whom a vendor's employee id stands for.</summary>
    [HttpGet("employee-options")]
    public async Task<IReadOnlyList<EmployeeOption>> EmployeeOptions(CancellationToken cancellationToken) =>
        await _integrations.ListEmployeeOptionsAsync(cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<IntegrationListItem>> Get(int id, CancellationToken cancellationToken) =>
        await _integrations.GetAsync(id, cancellationToken) is { } item ? item : NotFound();

    /// <summary>The integration as editable input.</summary>
    [HttpGet("{id:int}/input")]
    public async Task<ActionResult<IntegrationInput>> GetInput(int id, CancellationToken cancellationToken) =>
        await _integrations.GetInputAsync(id, cancellationToken) is { } input ? input : NotFound();

    [HttpPost]
    public async Task<ActionResult<CreatedResponse>> Create(IntegrationInput input, CancellationToken cancellationToken)
    {
        var result = await _integrations.CreateAsync(input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, IntegrationInput input, CancellationToken cancellationToken)
    {
        var result = await _integrations.UpdateAsync(id, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>The devices of every integration, each with its integration and the number of employee ids mapped on it.</summary>
    [HttpGet("devices")]
    public async Task<IReadOnlyList<DeviceItem>> AllDevices(CancellationToken cancellationToken) =>
        await _integrations.ListAllDevicesAsync(cancellationToken);

    [HttpGet("{id:int}/devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceItem>>> Devices(int id, CancellationToken cancellationToken) =>
        await _integrations.ListDevicesAsync(id, cancellationToken) is { } devices ? Ok(devices) : NotFound();

    [HttpGet("{id:int}/devices/{deviceId:int}")]
    public async Task<ActionResult<DeviceItem>> Device(int id, int deviceId, CancellationToken cancellationToken) =>
        await _integrations.GetDeviceAsync(id, deviceId, cancellationToken) is { } device ? device : NotFound();

    [HttpPost("{id:int}/devices")]
    public async Task<ActionResult<CreatedResponse>> AddDevice(int id, DeviceInput input, CancellationToken cancellationToken)
    {
        var result = await _integrations.AddDeviceAsync(id, input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpPut("{id:int}/devices/{deviceId:int}")]
    public async Task<IActionResult> UpdateDevice(int id, int deviceId, DeviceInput input, CancellationToken cancellationToken)
    {
        var result = await _integrations.UpdateDeviceAsync(id, deviceId, input, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [HttpDelete("{id:int}/devices/{deviceId:int}")]
    public async Task<IActionResult> DeleteDevice(int id, int deviceId, CancellationToken cancellationToken)
    {
        var result = await _integrations.DeleteDeviceAsync(id, deviceId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>A device's mappings: which employee each employee id from the vendor stands for on that device.</summary>
    [HttpGet("{id:int}/devices/{deviceId:int}/mappings")]
    public async Task<ActionResult<IReadOnlyList<MappingItem>>> Mappings(int id, int deviceId, CancellationToken cancellationToken) =>
        await _integrations.ListMappingsAsync(id, deviceId, cancellationToken) is { } mappings ? Ok(mappings) : NotFound();

    [HttpPost("{id:int}/devices/{deviceId:int}/mappings")]
    public async Task<ActionResult<CreatedResponse>> AddMapping(int id, int deviceId, MappingInput input, CancellationToken cancellationToken)
    {
        var result = await _integrations.AddMappingAsync(id, deviceId, input, cancellationToken);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new CreatedResponse { Id = result.Id!.Value }) : Failure(result);
    }

    [HttpDelete("{id:int}/devices/{deviceId:int}/mappings/{mappingId:int}")]
    public async Task<IActionResult> DeleteMapping(int id, int deviceId, int mappingId, CancellationToken cancellationToken)
    {
        var result = await _integrations.DeleteMappingAsync(id, deviceId, mappingId, cancellationToken);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    /// <summary>Syncs now, instead of at the next scheduled time. A provider failure is the outcome (200), not an error.</summary>
    [HttpPost("{id:int}/sync")]
    public async Task<ActionResult<SyncOutcomeResponse>> Sync(int id, CancellationToken cancellationToken) =>
        await _integrations.RunAsync(id, reconcile: false, cancellationToken) is { } outcome ? outcome : NotFound();

    /// <summary>Re-pulls the wider reconciliation window now.</summary>
    [HttpPost("{id:int}/reconcile")]
    public async Task<ActionResult<SyncOutcomeResponse>> Reconcile(int id, CancellationToken cancellationToken) =>
        await _integrations.RunAsync(id, reconcile: true, cancellationToken) is { } outcome ? outcome : NotFound();

    /// <summary>Queues the punches that failed for want of a time zone to be normalized again (after the integration's time zone or a device was set).</summary>
    [HttpPost("{id:int}/retry-failed")]
    public async Task<ActionResult<RetryFailedResponse>> RetryFailed(int id, CancellationToken cancellationToken) =>
        await _integrations.RetryFailedAsync(id, cancellationToken) is { } requeued ? new RetryFailedResponse { Requeued = requeued } : NotFound();

    /// <summary>Takes the punches flagged for an unrecognized device out of the calculation and queues them to be normalized again, now that the integration's device for uploaded files (or a registered device) can resolve them.</summary>
    [HttpPost("{id:int}/renormalize-flagged")]
    public async Task<ActionResult<RenormalizeFlaggedResponse>> RenormalizeFlagged(int id, CancellationToken cancellationToken) =>
        await _integrations.RenormalizeFlaggedAsync(id, cancellationToken) is { } outcome ? outcome : NotFound();

    /// <summary>
    /// The integration's raw punches, newest first, each beside the normalized punch it produced.
    /// Every call is written to the audit log. <paramref name="take"/> is at most 500 (default 100).
    /// </summary>
    /// <param name="batchId">Only the rows of this batch.</param>
    /// <param name="status">Only raw rows with this status: Pending, Normalized, Failed or Skipped.</param>
    /// <param name="take">How many rows, newest first.</param>
    [HttpGet("{id:int}/raw-mapping")]
    public async Task<ActionResult<IReadOnlyList<RawNormalizedMappingItem>>> RawMapping(int id, int? batchId, string? status, int take, CancellationToken cancellationToken)
    {
        RawTransactionProcessingStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RawTransactionProcessingStatus>(status, ignoreCase: true, out var value))
            {
                ModelState.AddModelError(nameof(status), $"The status must be one of: {string.Join(", ", Enum.GetNames<RawTransactionProcessingStatus>())}.");
                return ValidationProblem(ModelState);
            }

            parsed = value;
        }

        return await _integrations.ListRawToNormalizedAsync(id, new RawNormalizedFilter(batchId, parsed, take), cancellationToken) is { } rows ? Ok(rows) : NotFound();
    }

    /// <summary>How this integration's provider maps raw data: its fields, timestamp formats, event codes, and what is never stored.</summary>
    [HttpGet("{id:int}/raw-mapping/guide")]
    public async Task<ActionResult<ProviderMappingGuide>> RawMappingGuide(int id, CancellationToken cancellationToken) =>
        await _integrations.GetRawMappingGuideAsync(id, cancellationToken) is { } guide ? guide : NotFound();

    /// <summary>The integration's stored health, its circuit breaker and the KPIs of the last day.</summary>
    [HttpGet("{id:int}/health")]
    public async Task<ActionResult<IntegrationHealthReport>> GetHealth(int id, CancellationToken cancellationToken) =>
        await _health.GetIntegrationHealthAsync(id, cancellationToken) is { } report ? report : NotFound();

    private ActionResult Failure(SaveResult result)
    {
        if (result.NotFound)
        {
            return NotFound();
        }

        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }

        return ValidationProblem(ModelState);
    }
}
