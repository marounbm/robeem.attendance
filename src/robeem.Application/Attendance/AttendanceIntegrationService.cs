using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceIntegrationService : IAttendanceIntegrationService
{
    // A reference is a name, like "acme-clock-api" or "kv:acme/clock": not a connection string or a key.
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly IIntegrationSyncService _sync;
    private readonly IAttendanceAuditTrail _audit;

    public AttendanceIntegrationService(IRobeemDbContext db, IIntegrationSyncService sync, IAttendanceAuditTrail audit)
    {
        _db = db;
        _sync = sync;
        _audit = audit;
    }

    public async Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken) =>
        await Items(_db, _db.AttendanceIntegrations.AsNoTracking()).OrderBy(i => i.DisplayName).ToListAsync(cancellationToken);

    public async Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) =>
        await Items(_db, _db.AttendanceIntegrations.AsNoTracking().Where(i => i.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        return integration is null
            ? null
            : new IntegrationInput
            {
                ProviderId = integration.ProviderId,
                DisplayName = integration.DisplayName,
                ConnectionConfigRef = integration.ConnectionConfigRef,
                PollingScheduleCron = integration.PollingScheduleCron,
                Status = integration.Status.ToString(),
            };
    }

    public async Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken) =>
        await _db.AttendanceProviders
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.DisplayName)
            .Select(p => new ProviderOption { Id = p.Id, Key = p.ProviderKey, DisplayName = p.DisplayName, Manufacturer = p.Manufacturer, WebsiteUrl = p.WebsiteUrl, IsActive = p.IsActive })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Profiles
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.EmployeeNumber)
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.EmployeeNumber })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new EmployeeOption { Id = p.Id, Name = $"{p.FirstName} {p.LastName} ({p.EmployeeNumber})" }).ToList();
    }

    public async Task<SaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken)
    {
        var errors = Validate(input);
        var provider = await _db.AttendanceProviders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.ProviderId, cancellationToken);
        if (provider is null)
        {
            errors[nameof(IntegrationInput.ProviderId)] = "The selected provider does not exist.";
        }
        else if (!provider.IsActive)
        {
            errors[nameof(IntegrationInput.ProviderId)] = "This provider has no adapter yet, so it can't be used.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var integration = new AttendanceIntegration { ProviderId = input.ProviderId };
        Apply(input, integration);
        _db.AttendanceIntegrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Created '{integration.DisplayName}' ({provider!.ProviderKey}).");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(integration.Id);
    }

    public async Task<SaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null)
        {
            return SaveResult.Missing();
        }

        var errors = Validate(input);
        if (input.ProviderId != integration.ProviderId)
        {
            errors[nameof(IntegrationInput.ProviderId)] = "The provider can't be changed: its devices and punches belong to that adapter. Create a new integration instead.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{integration.DisplayName}; {integration.Status}; cron '{integration.PollingScheduleCron}'; ref '{integration.ConnectionConfigRef}'";
        Apply(input, integration);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Changed. Before: {before}. After: {integration.DisplayName}; {integration.Status}; cron '{integration.PollingScheduleCron}'; ref '{integration.ConnectionConfigRef}'.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(integration.Id);
    }

    public async Task<IReadOnlyList<DeviceItem>?> ListDevicesAsync(int integrationId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        var devices = await _db.AttendanceDevices
            .AsNoTracking()
            .Where(d => d.IntegrationId == integrationId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
        return devices
            .Select(d => new DeviceItem { Id = d.Id, ExternalDeviceId = d.ExternalDeviceId, Name = d.Name, DeviceType = d.DeviceType.ToString(), Model = d.Model, SoftwareVersion = d.SoftwareVersion, SdkVersion = d.SdkVersion, TimeZoneId = d.TimeZoneId, LastSeenAt = d.LastSeenAt })
            .ToList();
    }

    public async Task<SaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateDeviceAsync(integrationId, null, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var device = new AttendanceDevice { IntegrationId = integrationId };
        ApplyDevice(input, device);
        _db.AttendanceDevices.Add(device);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), device.Id, $"Device '{device.Name}' ({device.ExternalDeviceId}, {device.TimeZoneId}) added to integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(device.Id);
    }

    public async Task<SaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken)
    {
        var device = await _db.AttendanceDevices.SingleOrDefaultAsync(d => d.Id == deviceId && d.IntegrationId == integrationId, cancellationToken);
        if (device is null)
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateDeviceAsync(integrationId, deviceId, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{device.Name} ({device.ExternalDeviceId}, {device.TimeZoneId})";
        ApplyDevice(input, device);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), device.Id, $"Device changed. Before: {before}. After: {device.Name} ({device.ExternalDeviceId}, {device.TimeZoneId}).");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(device.Id);
    }

    public async Task<SaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken)
    {
        var device = await _db.AttendanceDevices.SingleOrDefaultAsync(d => d.Id == deviceId && d.IntegrationId == integrationId, cancellationToken);
        if (device is null)
        {
            return SaveResult.Missing();
        }

        _db.AttendanceDevices.Remove(device);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), device.Id, $"Device '{device.Name}' ({device.ExternalDeviceId}) removed from integration {integrationId}.");
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Punches refer to it (a restricted foreign key). Raw data is never touched, and never even read
            // here: the database says no, and the device is kept.
            _db.ChangeTracker.Clear();
            return SaveResult.Invalid("id", "This device has punches on record, so it can't be deleted. Keep it; rename it if it is retired.");
        }

        return SaveResult.Success(deviceId);
    }

    public async Task<IReadOnlyList<MappingItem>?> ListMappingsAsync(int integrationId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.ExternalEmployeeIdMappings
            .AsNoTracking()
            .Where(m => m.IntegrationId == integrationId)
            .OrderBy(m => m.ExternalEmployeeId)
            .Select(m => new { m.Id, m.ExternalEmployeeId, m.EmployeeId, m.Employee!.FirstName, m.Employee.LastName })
            .ToListAsync(cancellationToken);
        return rows.Select(m => new MappingItem { Id = m.Id, ExternalEmployeeId = m.ExternalEmployeeId, EmployeeId = m.EmployeeId, EmployeeName = $"{m.FirstName} {m.LastName}" }).ToList();
    }

    public async Task<SaveResult> AddMappingAsync(int integrationId, MappingInput input, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var external = (input.ExternalEmployeeId ?? string.Empty).Trim();
        var errors = new Dictionary<string, string>();
        if (external.Length == 0 || external.Length > 255)
        {
            errors[nameof(MappingInput.ExternalEmployeeId)] = "The vendor employee id is required (at most 255 characters).";
        }
        else if (await _db.ExternalEmployeeIdMappings.AnyAsync(m => m.IntegrationId == integrationId && m.ExternalEmployeeId == external, cancellationToken))
        {
            errors[nameof(MappingInput.ExternalEmployeeId)] = "This vendor employee id is already mapped in this integration.";
        }

        if (!await _db.Profiles.AnyAsync(p => p.Id == input.EmployeeId, cancellationToken))
        {
            errors[nameof(MappingInput.EmployeeId)] = "The selected employee does not exist.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var mapping = new ExternalEmployeeIdMapping { IntegrationId = integrationId, ExternalEmployeeId = external, EmployeeId = input.EmployeeId };
        _db.ExternalEmployeeIdMappings.Add(mapping);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(ExternalEmployeeIdMapping), mapping.Id, $"Vendor id '{external}' mapped to employee {input.EmployeeId} in integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(mapping.Id);
    }

    public async Task<SaveResult> DeleteMappingAsync(int integrationId, int mappingId, CancellationToken cancellationToken)
    {
        var mapping = await _db.ExternalEmployeeIdMappings.SingleOrDefaultAsync(m => m.Id == mappingId && m.IntegrationId == integrationId, cancellationToken);
        if (mapping is null)
        {
            return SaveResult.Missing();
        }

        _db.ExternalEmployeeIdMappings.Remove(mapping);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(ExternalEmployeeIdMapping), mapping.Id, $"Mapping of vendor id '{mapping.ExternalEmployeeId}' to employee {mapping.EmployeeId} removed from integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(mappingId);
    }

    public async Task<SyncOutcomeResponse?> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        _audit.Record(AttendanceAuditActions.IntegrationRunRequested, nameof(AttendanceIntegration), integrationId, reconcile ? "Reconciliation requested." : "Sync requested.");
        await _db.SaveChangesAsync(cancellationToken);

        var outcome = reconcile
            ? await _sync.ReconcileAsync(integrationId, cancellationToken)
            : await _sync.SyncAsync(integrationId, cancellationToken);
        return new SyncOutcomeResponse { Status = outcome.Status.ToString(), BatchId = outcome.BatchId, RecordsIngested = outcome.RecordsIngested, Message = outcome.Message };
    }

    private static IQueryable<IntegrationListItem> Items(IRobeemDbContext db, IQueryable<AttendanceIntegration> integrations) =>
        integrations.Select(i => new IntegrationListItem
        {
            Id = i.Id,
            DisplayName = i.DisplayName,
            ProviderId = i.ProviderId,
            ProviderKey = i.Provider!.ProviderKey,
            ProviderName = i.Provider.DisplayName,
            Status = i.Status.ToString(),
            HealthStatus = i.HealthStatus.ToString(),
            ConnectionConfigRef = i.ConnectionConfigRef,
            PollingScheduleCron = i.PollingScheduleCron,
            LastSuccessfulSyncAt = i.LastSuccessfulSyncAt,
            DeviceCount = db.AttendanceDevices.Count(d => d.IntegrationId == i.Id),
            MappingCount = db.ExternalEmployeeIdMappings.Count(m => m.IntegrationId == i.Id),
        });

    private static void Apply(IntegrationInput input, AttendanceIntegration integration)
    {
        integration.DisplayName = input.DisplayName.Trim();
        integration.ConnectionConfigRef = (input.ConnectionConfigRef ?? string.Empty).Trim();
        integration.PollingScheduleCron = string.IsNullOrWhiteSpace(input.PollingScheduleCron) ? null : input.PollingScheduleCron.Trim();
        integration.Status = Enum.Parse<AttendanceIntegrationStatus>(input.Status, ignoreCase: true);
    }

    private static void ApplyDevice(DeviceInput input, AttendanceDevice device)
    {
        device.ExternalDeviceId = input.ExternalDeviceId.Trim();
        device.Name = input.Name.Trim();
        device.DeviceType = Enum.Parse<AttendanceDeviceType>(input.DeviceType, ignoreCase: true);
        device.Model = (input.Model ?? string.Empty).Trim();
        device.SoftwareVersion = (input.SoftwareVersion ?? string.Empty).Trim();
        device.SdkVersion = (input.SdkVersion ?? string.Empty).Trim();
        device.TimeZoneId = input.TimeZoneId.Trim();
    }

    private static Dictionary<string, string> Validate(IntegrationInput input)
    {
        var errors = new Dictionary<string, string>();

        var name = (input.DisplayName ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(IntegrationInput.DisplayName)] = "A name is required (at most 255 characters).";
        }

        var reference = (input.ConnectionConfigRef ?? string.Empty).Trim();
        if (reference.Length > 0 && (reference.Length > 255 || !ReferencePattern.IsMatch(reference)))
        {
            errors[nameof(IntegrationInput.ConnectionConfigRef)] = "Give the name the secret is kept under (letters, digits and . _ : / -), never the secret itself.";
        }

        var cron = (input.PollingScheduleCron ?? string.Empty).Trim();
        if (cron.Length > 100)
        {
            errors[nameof(IntegrationInput.PollingScheduleCron)] = "The schedule can be at most 100 characters.";
        }
        else if (cron.Length > 0 && !CronSchedule.TryParse(cron, out _))
        {
            errors[nameof(IntegrationInput.PollingScheduleCron)] = "Not a valid cron expression: five fields (minute hour day month weekday), or six with seconds first.";
        }

        // Failed is the system's to set (and a person clears it by choosing Active).
        if (!Enum.TryParse<AttendanceIntegrationStatus>(input.Status, ignoreCase: true, out var status)
            || status is not (AttendanceIntegrationStatus.Active or AttendanceIntegrationStatus.Paused))
        {
            errors[nameof(IntegrationInput.Status)] = "The status must be Active or Paused.";
        }

        return errors;
    }

    private async Task<Dictionary<string, string>> ValidateDeviceAsync(int integrationId, int? existingId, DeviceInput input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();

        var external = (input.ExternalDeviceId ?? string.Empty).Trim();
        if (external.Length == 0 || external.Length > 100)
        {
            errors[nameof(DeviceInput.ExternalDeviceId)] = "The device id is required (at most 100 characters).";
        }
        else if (await _db.AttendanceDevices.AnyAsync(d => d.IntegrationId == integrationId && d.ExternalDeviceId == external && d.Id != existingId, cancellationToken))
        {
            errors[nameof(DeviceInput.ExternalDeviceId)] = "Another device of this integration already has this id.";
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(DeviceInput.Name)] = "A name is required (at most 255 characters).";
        }

        if (!Enum.TryParse<AttendanceDeviceType>(input.DeviceType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            errors[nameof(DeviceInput.DeviceType)] = $"The type must be one of: {string.Join(", ", Enum.GetNames<AttendanceDeviceType>())}.";
        }

        if ((input.Model ?? string.Empty).Trim().Length > 100)
        {
            errors[nameof(DeviceInput.Model)] = "The model can be at most 100 characters.";
        }

        if ((input.SoftwareVersion ?? string.Empty).Trim().Length > 100)
        {
            errors[nameof(DeviceInput.SoftwareVersion)] = "The software version can be at most 100 characters.";
        }

        if ((input.SdkVersion ?? string.Empty).Trim().Length > 50)
        {
            errors[nameof(DeviceInput.SdkVersion)] = "The SDK version can be at most 50 characters.";
        }

        var zone = (input.TimeZoneId ?? string.Empty).Trim();
        if (zone.Length == 0 || zone.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
        {
            errors[nameof(DeviceInput.TimeZoneId)] = "An IANA time zone is required, such as Asia/Beirut or Europe/London.";
        }

        return errors;
    }
}
