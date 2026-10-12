using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceIntegrationService : IAttendanceIntegrationService
{
    private const int MaxRawMappingTake = 500;

    // A reference is a name, like "acme-clock-api" or "kv:acme/clock": not a connection string or a key.
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly IIntegrationSyncService _sync;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IServiceProvider _services;

    public AttendanceIntegrationService(IRobeemDbContext db, IIntegrationSyncService sync, IAttendanceAuditTrail audit, IServiceProvider services)
    {
        _db = db;
        _sync = sync;
        _audit = audit;
        _services = services;
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
                Transport = integration.Transport.ToString(),
                FileFormatId = integration.FileFormatId,
                DefaultDeviceId = integration.DefaultDeviceId,
                TimeZoneId = integration.TimeZoneId,
                PollingScheduleCron = integration.PollingScheduleCron,
                Status = integration.Status.ToString(),
            };
    }

    public async Task<IReadOnlyList<ProviderOption>> ListProvidersAsync(CancellationToken cancellationToken) =>
        (await _db.AttendanceProviders
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.DisplayName)
            .ToListAsync(cancellationToken))
        .Select(p => new ProviderOption { Id = p.Id, Key = p.ProviderKey, DisplayName = p.DisplayName, Manufacturer = p.Manufacturer, WebsiteUrl = p.WebsiteUrl, SupportedTransports = SupportedTransportsOf(p), IsActive = p.IsActive })
        .ToList();

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

        if (provider is not null)
        {
            ValidateTransport(input, provider, errors);
        }

        await ValidateFileFormatAsync(input, null, errors, cancellationToken);
        await ValidateDefaultDeviceAsync(input, null, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var integration = new AttendanceIntegration { ProviderId = input.ProviderId };
        Apply(input, integration);
        _db.AttendanceIntegrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Created '{integration.DisplayName}' ({provider!.ProviderKey}, {integration.Transport}).");
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
        else
        {
            var provider = await _db.AttendanceProviders.AsNoTracking().SingleAsync(p => p.Id == integration.ProviderId, cancellationToken);
            ValidateTransport(input, provider, errors);
        }

        await ValidateFileFormatAsync(input, integration.FileFormatId, errors, cancellationToken);
        await ValidateDefaultDeviceAsync(input, integration.Id, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{integration.DisplayName}; {integration.Status}; {integration.Transport}; format {integration.FileFormatId?.ToString() ?? "none"}; device {integration.DefaultDeviceId?.ToString() ?? "none"}; zone '{integration.TimeZoneId}'; cron '{integration.PollingScheduleCron}'; ref '{integration.ConnectionConfigRef}'";
        Apply(input, integration);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Changed. Before: {before}. After: {integration.DisplayName}; {integration.Status}; {integration.Transport}; format {integration.FileFormatId?.ToString() ?? "none"}; device {integration.DefaultDeviceId?.ToString() ?? "none"}; zone '{integration.TimeZoneId}'; cron '{integration.PollingScheduleCron}'; ref '{integration.ConnectionConfigRef}'.");
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

        if (await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId && i.DefaultDeviceId == deviceId, cancellationToken))
        {
            return SaveResult.Invalid("id", "This is the integration's device for uploaded files. Choose another device for uploaded files first, then delete it.");
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

    public async Task<int?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        // Only a punch that failed for want of a time zone: one rejected at ingestion (an unparseable
        // timestamp, a payload the sanitizer refused) would fail again. Status and error are the only
        // columns of a raw row that may change (invariant 2); the normalization job picks the rows up.
        var failed = await _db.RawAttendanceTransactions
            .Where(r => r.IntegrationId == integrationId
                && r.ProcessingStatus == RawTransactionProcessingStatus.Failed
                && r.ProcessingError != null
                && r.ProcessingError.StartsWith(NormalizationFailures.NoTimeZonePrefix))
            .ToListAsync(cancellationToken);
        foreach (var raw in failed)
        {
            raw.ProcessingStatus = RawTransactionProcessingStatus.Pending;
            raw.ProcessingError = null;
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integrationId, $"{failed.Count} punch(es) that failed for want of a time zone were queued to be normalized again.");
        await _db.SaveChangesAsync(cancellationToken);
        return failed.Count;
    }

    public async Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new { i.Transport, i.DefaultDeviceId })
            .SingleOrDefaultAsync(cancellationToken);
        if (integration is null)
        {
            return null;
        }

        var defaultDeviceInForce = integration.Transport == AttendanceTransport.FileUpload && integration.DefaultDeviceId is not null;
        var registered = (await _db.AttendanceDevices.AsNoTracking()
            .Where(d => d.IntegrationId == integrationId)
            .Select(d => d.ExternalDeviceId)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

        // A punch is taken up again only when its device can be resolved now (by the integration's device for
        // uploaded files, or by a device registered since); otherwise it would be flagged again.
        var flagged = await _db.NormalizedAttendanceTransactions
            .Include(n => n.RawTransaction)
            .Where(n => n.IntegrationId == integrationId
                && n.ExceptionStatus == TransactionExceptionStatus.FlaggedUnrecognizedDevice
                && n.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded)
            .ToListAsync(cancellationToken);
        var resolvable = flagged
            .Where(n => defaultDeviceInForce || (n.RawTransaction!.DeviceExternalId is { } externalId && registered.Contains(externalId)))
            .ToList();

        // Nothing is rewritten: the old punch is only taken out of the calculation (Excluded, which stays
        // as history), and its raw row goes back to Pending so normalization writes a new punch (invariants 2 and 7).
        foreach (var punch in resolvable)
        {
            punch.ProcessingStatus = NormalizedTransactionProcessingStatus.Excluded;
            punch.RawTransaction!.ProcessingStatus = RawTransactionProcessingStatus.Pending;
            punch.RawTransaction.ProcessingError = null;
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integrationId, $"{resolvable.Count} punch(es) flagged for an unrecognized device were queued to be normalized again.");
        await _db.SaveChangesAsync(cancellationToken);
        return new RenormalizeFlaggedResponse
        {
            Requeued = resolvable.Count,
            Unresolved = flagged.Count - resolvable.Count,
            HasDeviceForUploads = defaultDeviceInForce,
            Transport = integration.Transport.ToString(),
        };
    }

    public async Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawToNormalizedAsync(int integrationId, RawNormalizedFilter filter, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new { i.DisplayName, ProviderKey = i.Provider!.ProviderKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (integration is null)
        {
            return null;
        }

        var take = Math.Clamp(filter.Take <= 0 ? 100 : filter.Take, 1, MaxRawMappingTake);

        // A direct read of raw and normalized attendance outside the engine: audited, and saved
        // before the read, so the read is on record even if it then fails.
        _audit.Record(
            AttendanceAuditActions.AttendanceDataRead,
            $"{nameof(RawAttendanceTransaction)},{nameof(NormalizedAttendanceTransaction)}",
            integrationId,
            $"Raw to normalized mapping of integration {integrationId} (batch {filter.BatchId?.ToString() ?? "any"}, status {filter.Status?.ToString() ?? "any"}, first {take}).");
        await _db.SaveChangesAsync(cancellationToken);

        var raws = _db.RawAttendanceTransactions.AsNoTracking().Where(r => r.IntegrationId == integrationId);
        if (filter.BatchId is { } batchId)
        {
            raws = raws.Where(r => r.ImportBatchId == batchId);
        }

        if (filter.Status is { } status)
        {
            raws = raws.Where(r => r.ProcessingStatus == status);
        }

        // A left join, so a raw row that was not normalized (waiting, failed or skipped) still shows. Newest first.
        var rows = await (
            from r in raws
            join n in _db.NormalizedAttendanceTransactions.AsNoTracking().Where(x => x.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded) on r.Id equals n.RawTransactionId into normalized
            from n in normalized.DefaultIfEmpty()
            orderby r.Id descending
            select new { Raw = r, Normalized = n })
            .Take(take)
            .ToListAsync(cancellationToken);

        var employeeIds = rows.Where(x => x.Normalized?.EmployeeId != null).Select(x => x.Normalized!.EmployeeId!.Value).Distinct().ToList();
        var employees = employeeIds.Count == 0
            ? []
            : await _db.Profiles.AsNoTracking()
                .Where(p => employeeIds.Contains(p.Id))
                .Select(p => new { p.Id, p.EmployeeNumber, p.FirstName, p.LastName })
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        var deviceIds = rows.Where(x => x.Normalized?.DeviceId != null).Select(x => x.Normalized!.DeviceId!.Value).Distinct().ToList();
        var devices = deviceIds.Count == 0
            ? []
            : await _db.AttendanceDevices.AsNoTracking()
                .Where(d => deviceIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Title })
                .ToDictionaryAsync(d => d.Id, d => d.Title, cancellationToken);

        return rows.Select(x =>
        {
            var n = x.Normalized;
            var employee = n?.EmployeeId is { } employeeId && employees.TryGetValue(employeeId, out var e) ? e : null;
            return new RawNormalizedMappingItem
            {
                ProviderKey = integration.ProviderKey,
                IntegrationId = integrationId,
                IntegrationName = integration.DisplayName,
                BatchId = x.Raw.ImportBatchId,
                RawId = x.Raw.Id,
                ExternalTransactionId = x.Raw.ExternalTransactionId,
                ExternalEmployeeId = x.Raw.ExternalEmployeeId,
                DeviceExternalId = x.Raw.DeviceExternalId,
                TimestampRaw = x.Raw.TransactionTimestampRaw,
                TypeRaw = x.Raw.TransactionTypeRaw,
                DirectionRaw = x.Raw.DirectionRaw,
                RawStatus = x.Raw.ProcessingStatus.ToString(),
                ProcessingError = x.Raw.ProcessingError,
                Payload = x.Raw.RawPayload,
                NormalizedId = n?.Id,
                EmployeeNumber = employee?.EmployeeNumber,
                EmployeeName = employee is null ? null : $"{employee.FirstName} {employee.LastName}",
                DeviceName = n?.DeviceId is { } deviceId && devices.TryGetValue(deviceId, out var deviceName) ? deviceName : null,
                LocalTime = n?.EventTimestampLocal,
                TimeZoneId = n?.TimeZoneId,
                EventType = n?.NormalizedEventType.ToString(),
                Confidence = n?.ConfidenceStatus.ToString(),
                DuplicateStatus = n?.DuplicateStatus.ToString(),
                ExceptionStatus = n?.ExceptionStatus.ToString(),
            };
        }).ToList();
    }

    public async Task<ProviderMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken)
    {
        var providerKey = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => i.Provider!.ProviderKey)
            .SingleOrDefaultAsync(cancellationToken);
        if (providerKey is null)
        {
            return null;
        }

        return (_services.GetKeyedService<IAttendanceProvider>(providerKey) as IAttendanceMappingGuide)?.DescribeMapping()
            ?? new ProviderMappingGuide { ProviderKey = providerKey, Summary = "This provider's adapter does not describe its mapping." };
    }

    private static IQueryable<IntegrationListItem> Items(IRobeemDbContext db, IQueryable<AttendanceIntegration> integrations) =>
        integrations.Select(i => new IntegrationListItem
        {
            Id = i.Id,
            DisplayName = i.DisplayName,
            ProviderId = i.ProviderId,
            ProviderKey = i.Provider!.ProviderKey,
            ProviderName = i.Provider.DisplayName,
            Transport = i.Transport.ToString(),
            FileFormatId = i.FileFormatId,
            FileFormatName = i.FileFormat!.Name,
            DefaultDeviceId = i.DefaultDeviceId,
            DefaultDeviceName = i.DefaultDevice!.Name,
            TimeZoneId = i.TimeZoneId,
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
        integration.Transport = Enum.Parse<AttendanceTransport>(input.Transport, ignoreCase: true);
        integration.FileFormatId = integration.Transport == AttendanceTransport.FileUpload ? input.FileFormatId : null;
        integration.DefaultDeviceId = integration.Transport == AttendanceTransport.FileUpload ? input.DefaultDeviceId : null;
        integration.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? null : input.TimeZoneId.Trim();

        // Only an integration that pulls has a schedule; an upload or a webhook is pushed to robeem.
        integration.PollingScheduleCron = string.IsNullOrWhiteSpace(input.PollingScheduleCron) || !IsPull(integration.Transport) ? null : input.PollingScheduleCron.Trim();
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

    private static bool IsPull(AttendanceTransport transport) =>
        transport is AttendanceTransport.Sftp or AttendanceTransport.RestApi or AttendanceTransport.Database or AttendanceTransport.DeviceSdk;

    /// <summary>The provider's own transports first, then manual file upload, which any provider can be given (the file is read by a custom format, or the generic layout).</summary>
    private static IReadOnlyList<string> SupportedTransportsOf(AttendanceProvider provider)
    {
        var transports = Enum.GetValues<AttendanceTransport>().Where(provider.SupportsTransport).Select(t => t.ToString()).ToList();
        if (!transports.Contains(nameof(AttendanceTransport.FileUpload)))
        {
            transports.Add(nameof(AttendanceTransport.FileUpload));
        }

        return transports;
    }

    private static void ValidateTransport(IntegrationInput input, AttendanceProvider provider, Dictionary<string, string> errors)
    {
        if (!Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport) || !Enum.IsDefined(transport))
        {
            errors[nameof(IntegrationInput.Transport)] = $"Choose how the integration receives attendance: {string.Join(", ", Enum.GetNames<AttendanceTransport>())}.";
        }
        else if (!SupportedTransportsOf(provider).Contains(transport.ToString()))
        {
            var supported = SupportedTransportsOf(provider);
            errors[nameof(IntegrationInput.Transport)] = $"{provider.DisplayName} can't receive attendance by {transport}. It supports: {(supported.Count == 0 ? "nothing yet" : string.Join(", ", supported))}.";
        }
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

        var zone = (input.TimeZoneId ?? string.Empty).Trim();
        if (zone.Length > 64 || (zone.Length > 0 && !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)))
        {
            errors[nameof(IntegrationInput.TimeZoneId)] = "An IANA time zone such as Asia/Beirut or Europe/London, or empty.";
        }

        // Failed is the system's to set (and a person clears it by choosing Active).
        if (!Enum.TryParse<AttendanceIntegrationStatus>(input.Status, ignoreCase: true, out var status)
            || status is not (AttendanceIntegrationStatus.Active or AttendanceIntegrationStatus.Paused))
        {
            errors[nameof(IntegrationInput.Status)] = "The status must be Active or Paused.";
        }

        return errors;
    }

    private async Task ValidateFileFormatAsync(IntegrationInput input, int? currentFormatId, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        // A format only applies to manual uploads; for any other transport it is dropped, not refused.
        if (input.FileFormatId is not { } formatId
            || !Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport)
            || transport != AttendanceTransport.FileUpload)
        {
            return;
        }

        // An integration may keep a format that has since been retired; a new choice must be an active one.
        var usable = await _db.AttendanceFileFormats.AnyAsync(f => f.Id == formatId && (f.IsActive || f.Id == currentFormatId), cancellationToken);
        if (!usable)
        {
            errors[nameof(IntegrationInput.FileFormatId)] = "The selected file format does not exist or is not active.";
        }
    }

    private async Task ValidateDefaultDeviceAsync(IntegrationInput input, int? integrationId, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        // Like a file format, the device only applies to manual uploads; for any other transport it is dropped, not refused.
        if (input.DefaultDeviceId is not { } deviceId
            || !Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport)
            || transport != AttendanceTransport.FileUpload)
        {
            return;
        }

        if (integrationId is null)
        {
            errors[nameof(IntegrationInput.DefaultDeviceId)] = "Create the integration first, add its device, then choose it here.";
        }
        else if (!await _db.AttendanceDevices.AnyAsync(d => d.Id == deviceId && d.IntegrationId == integrationId, cancellationToken))
        {
            errors[nameof(IntegrationInput.DefaultDeviceId)] = "Choose one of this integration's own devices.";
        }
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
