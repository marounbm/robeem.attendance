using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceIntegrationService : IAttendanceIntegrationService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;

    /// <summary>The name of the connection an integration is created with.</summary>
    private const string DefaultConnectionName = "Default";

    // A reference is a name, like "acme-clock-api" or "kv:acme/clock": not a connection string or a key.
    private static readonly Regex ReferencePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]*$", RegexOptions.CultureInvariant);

    private readonly IRobeemDbContext _db;
    private readonly ICatalogReader _catalog;
    private readonly IIntegrationSyncService _sync;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IServiceProvider _services;

    public AttendanceIntegrationService(IRobeemDbContext db, ICatalogReader catalog, IIntegrationSyncService sync, IAttendanceAuditTrail audit, IServiceProvider services)
    {
        _db = db;
        _catalog = catalog;
        _sync = sync;
        _audit = audit;
        _services = services;
    }

    public async Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await QueryItemsAsync(_db, _catalog, _db.AttendanceIntegrations.AsNoTracking().OrderBy(i => i.DisplayName), cancellationToken);
        await NameTransportsAsync(items, cancellationToken);
        return items;
    }

    public async Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var item = (await QueryItemsAsync(_db, _catalog, _db.AttendanceIntegrations.AsNoTracking().Where(i => i.Id == id), cancellationToken)).SingleOrDefault();
        if (item is not null)
        {
            await NameTransportsAsync([item], cancellationToken);
        }

        return item;
    }

    public async Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking().Include(i => i.Connections).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null)
        {
            return null;
        }

        // The connection fields of the form are the default connection's; an integration with none reads as a plain upload.
        var connection = integration.Connections.FirstOrDefault(c => c.IsDefault);
        return new IntegrationInput
        {
            ConnectorKey = integration.ConnectorKey,
            DisplayName = integration.DisplayName,
            ConnectionConfigRef = connection?.SecretRef ?? string.Empty,
            PayloadKeyRef = integration.PayloadKeyRef,
            Transport = (connection?.Transport ?? AttendanceTransport.FileUpload).ToString(),
            AuthMethod = (connection?.AuthMethod ?? ConnectionAuthMethod.None).ToString(),
            Settings = ConnectionSettingsValidator.Parse(connection?.Settings),
            DefaultDeviceId = integration.DefaultDeviceId,
            TimeZoneId = integration.TimeZoneId,
            PollingScheduleCron = connection?.PollingSchedule,
            Status = integration.Status.ToString(),
        };
    }

    public async Task<IReadOnlyList<ConnectorOption>> ListConnectorsAsync(CancellationToken cancellationToken)
    {
        var connectors = await _catalog.ListConnectorsAsync(cancellationToken);
        var definitions = await _catalog.ListTransportsAsync(cancellationToken);
        return connectors
            .Select(c => new ConnectorOption
            {
                Id = c.Id,
                Key = c.ConnectorKey,
                DisplayName = c.DisplayName,
                VendorKey = c.VendorKey,
                VendorName = c.VendorName,
                Manufacturer = c.VendorManufacturer,
                WebsiteUrl = c.VendorWebsiteUrl,
                SupportedTransports = SupportedTransportsOf(c.Transports, definitions),
                IsActive = c.IsUsable,
                EncryptionKind = c.EncryptionKind.ToString(),
            })
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.VendorName)
            .ThenBy(c => c.DisplayName)
            .ToList();
    }

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
        var connectorKey = (input.ConnectorKey ?? string.Empty).Trim();
        var connector = await FindUsableConnectorAsync(connectorKey, errors, cancellationToken);

        if (connector is not null)
        {
            await ValidateTransportAsync(input, connector, null, errors, cancellationToken);
            ValidatePayloadKey(input, connector, errors);
        }

        await ValidateDefaultDeviceAsync(input, null, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var integration = new AttendanceIntegration { ConnectorKey = connectorKey };
        var connection = new AttendanceConnection { Name = DefaultConnectionName, IsDefault = true };
        integration.Connections.Add(connection);
        Apply(input, integration, connection);
        _db.AttendanceIntegrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Created '{integration.DisplayName}' ({connector!.ConnectorKey}, {connection.Transport}).");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(integration.Id);
    }

    public async Task<DeviceSetupResult> SetUpDeviceAsync(DeviceSetupInput input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var connectorKey = (input.ConnectorKey ?? string.Empty).Trim();
        var connector = await FindUsableConnectorAsync(connectorKey, errors, cancellationToken);

        // The integration is the tenant's first active one for the connector. A person sets devices up from the vendor down and never
        // names it; when there is none, one is made below, with the connection entered here as its default.
        var integration = connector is null
            ? null
            : await _db.AttendanceIntegrations.Include(i => i.Connections)
                .Where(i => i.ConnectorKey == connectorKey && i.Status == AttendanceIntegrationStatus.Active)
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var device = input.Device ?? new DeviceInput();
        device.ConnectionId = input.ConnectionId;
        AttendanceConnection? existingConnection = null;
        IntegrationInput? connectionFields = null;
        if (input.ConnectionId is { } connectionId)
        {
            existingConnection = integration?.Connections.FirstOrDefault(c => c.Id == connectionId);
            if (existingConnection is not { IsActive: true })
            {
                errors[nameof(DeviceSetupInput.ConnectionId)] = "Choose one of this connector's own connections that is switched on, or enter a new one.";
            }
        }
        else if (connector is not null)
        {
            // The connection fields are held to what the transport declares, exactly as on the integration form.
            connectionFields = new IntegrationInput
            {
                ConnectorKey = connectorKey,
                DisplayName = connector.DisplayName,
                ConnectionConfigRef = input.ConnectionConfigRef,
                PayloadKeyRef = integration is null ? input.PayloadKeyRef : null,
                Transport = input.Transport,
                AuthMethod = input.AuthMethod,
                Settings = input.Settings,
                PollingScheduleCron = input.PollingScheduleCron,
                Status = nameof(AttendanceIntegrationStatus.Active),
            };
            foreach (var (field, message) in Validate(connectionFields))
            {
                errors[field] = message;
            }

            await ValidateTransportAsync(connectionFields, connector, null, errors, cancellationToken);
            if (integration is null)
            {
                ValidatePayloadKey(connectionFields, connector, errors);
            }
        }

        foreach (var (field, message) in await ValidateDeviceAsync(integration?.Id ?? 0, null, null, device, cancellationToken))
        {
            errors[$"{nameof(DeviceSetupInput.Device)}.{field}"] = message;
        }

        if (errors.Count > 0)
        {
            return new DeviceSetupResult(SaveResult.Invalid(errors));
        }

        // Everything that is made is saved together: no integration is left behind by a device that was refused.
        var integrationCreated = integration is null;
        AttendanceConnection? madeConnection = null;
        if (integration is null)
        {
            integration = new AttendanceIntegration { ConnectorKey = connectorKey };
            madeConnection = new AttendanceConnection { Name = DefaultConnectionName, IsDefault = true };
            integration.Connections.Add(madeConnection);
            Apply(connectionFields!, integration, madeConnection);
            _db.AttendanceIntegrations.Add(integration);
        }
        else if (existingConnection is null)
        {
            madeConnection = new AttendanceConnection
            {
                Integration = integration,
                Name = UniqueConnectionName(integration, $"{device.Name.Trim()} connection"),
                IsDefault = !integration.Connections.Any(c => c.IsDefault),
            };
            var connectionInput = ToConnectionInput(connectionFields!);
            connectionInput.Name = madeConnection.Name;
            ApplyConnection(connectionInput, madeConnection);
            _db.AttendanceConnections.Add(madeConnection);
        }

        var newDevice = new AttendanceDevice { Integration = integration };
        ApplyDevice(device, newDevice);

        // The default connection needs no pointer; a connection made for this device alone is the device's own.
        if (madeConnection is { IsDefault: false })
        {
            newDevice.Connection = madeConnection;
        }

        _db.AttendanceDevices.Add(newDevice);
        await _db.SaveChangesAsync(cancellationToken);

        if (integrationCreated)
        {
            _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Created '{integration.DisplayName}' ({connectorKey}, {madeConnection!.Transport}) while setting up device '{newDevice.Name}'.");
        }
        else if (madeConnection is not null)
        {
            _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnection), madeConnection.Id, $"Connection '{madeConnection.Name}' ({madeConnection.Transport}/{madeConnection.AuthMethod}) added to integration {integration.Id} while setting up device '{newDevice.Name}'; secret reference '{madeConnection.SecretRef}'.");
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), newDevice.Id, $"Device '{newDevice.Name}' ({newDevice.ExternalDeviceId}, {newDevice.TimeZoneId}) added to integration {integration.Id}.");
        await QueueTimeZoneFailuresAsync(integration.Id, $"device '{newDevice.ExternalDeviceId}' was registered", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new DeviceSetupResult(SaveResult.Success(newDevice.Id), integration.Id, integrationCreated, madeConnection is not null && !integrationCreated);
    }

    public async Task<SaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.Include(i => i.Connections).SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null)
        {
            return SaveResult.Missing();
        }

        // An integration that predates connections gets its default one now.
        var connection = integration.Connections.FirstOrDefault(c => c.IsDefault);
        var errors = Validate(input);
        if (!string.Equals((input.ConnectorKey ?? string.Empty).Trim(), integration.ConnectorKey, StringComparison.Ordinal))
        {
            errors[nameof(IntegrationInput.ConnectorKey)] = "The connector can't be changed: its devices and punches belong to that adapter. Create a new integration instead.";
        }
        else if (await _catalog.GetConnectorAsync(integration.ConnectorKey, cancellationToken) is { } connector)
        {
            await ValidateTransportAsync(input, connector, (connection?.Transport ?? AttendanceTransport.FileUpload).ToString(), errors, cancellationToken);
            ValidatePayloadKey(input, connector, errors);
        }

        await ValidateDefaultDeviceAsync(input, integration.Id, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        if (connection is null)
        {
            connection = new AttendanceConnection { Name = DefaultConnectionName, IsDefault = true };
            integration.Connections.Add(connection);
        }

        var before = Describe(integration, connection);
        Apply(input, integration, connection);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integration.Id, $"Changed. Before: {before}. After: {Describe(integration, connection)}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(integration.Id);
    }

    public async Task<IReadOnlyList<DeviceItem>?> ListDevicesAsync(int integrationId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        return await QueryDevicesAsync(integrationId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken) =>
        await QueryDevicesAsync(null, cancellationToken);

    public async Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) =>
        (await QueryDevicesAsync(integrationId, cancellationToken)).FirstOrDefault(d => d.Id == deviceId);

    public async Task<SaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var errors = await ValidateDeviceAsync(integrationId, null, null, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var device = new AttendanceDevice { IntegrationId = integrationId };
        ApplyDevice(input, device);
        _db.AttendanceDevices.Add(device);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), device.Id, $"Device '{device.Name}' ({device.ExternalDeviceId}, {device.TimeZoneId}) added to integration {integrationId}.");

        // Punches that failed because this device was unknown can now be read in its time zone.
        await QueueTimeZoneFailuresAsync(integrationId, $"device '{device.ExternalDeviceId}' was registered", cancellationToken);
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

        var errors = await ValidateDeviceAsync(integrationId, deviceId, device.DeviceTypeName, input, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{device.Name} ({device.ExternalDeviceId}, {device.TimeZoneId})";
        ApplyDevice(input, device);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceDevice), device.Id, $"Device changed. Before: {before}. After: {device.Name} ({device.ExternalDeviceId}, {device.TimeZoneId}).");

        // A device whose id or time zone just changed may be the one punches failed for.
        await QueueTimeZoneFailuresAsync(integrationId, $"device '{device.ExternalDeviceId}' was changed", cancellationToken);
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

        // No cascade (CLAUDE.md §8): the mappings a person typed in are removed on purpose, not by deleting the device.
        var mappings = await _db.ExternalEmployeeIdMappings.CountAsync(m => m.DeviceId == deviceId, cancellationToken);
        if (mappings > 0)
        {
            return SaveResult.Invalid("id", $"This device has {mappings} employee-id mapping(s). Remove them first, then delete the device.");
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

    public async Task<IReadOnlyList<MappingItem>?> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceDevices.AnyAsync(d => d.Id == deviceId && d.IntegrationId == integrationId, cancellationToken))
        {
            return null;
        }

        var rows = await _db.ExternalEmployeeIdMappings
            .AsNoTracking()
            .Where(m => m.DeviceId == deviceId)
            .OrderBy(m => m.ExternalEmployeeId)
            .Select(m => new { m.Id, m.ExternalEmployeeId, m.EmployeeId, m.Employee!.FirstName, m.Employee.LastName })
            .ToListAsync(cancellationToken);
        return rows.Select(m => new MappingItem { Id = m.Id, ExternalEmployeeId = m.ExternalEmployeeId, EmployeeId = m.EmployeeId, EmployeeName = $"{m.FirstName} {m.LastName}" }).ToList();
    }

    public async Task<SaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceDevices.AnyAsync(d => d.Id == deviceId && d.IntegrationId == integrationId, cancellationToken))
        {
            return SaveResult.Missing();
        }

        var external = (input.ExternalEmployeeId ?? string.Empty).Trim();
        var errors = new Dictionary<string, string>();
        if (external.Length == 0 || external.Length > 255)
        {
            errors[nameof(MappingInput.ExternalEmployeeId)] = "The vendor employee id is required (at most 255 characters).";
        }
        else if (await _db.ExternalEmployeeIdMappings.AnyAsync(m => m.DeviceId == deviceId && m.ExternalEmployeeId == external, cancellationToken))
        {
            errors[nameof(MappingInput.ExternalEmployeeId)] = "This vendor employee id is already mapped on this device.";
        }

        if (!await _db.Profiles.AnyAsync(p => p.Id == input.EmployeeId, cancellationToken))
        {
            errors[nameof(MappingInput.EmployeeId)] = "The selected employee does not exist.";
        }

        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var mapping = new ExternalEmployeeIdMapping { DeviceId = deviceId, ExternalEmployeeId = external, EmployeeId = input.EmployeeId };
        _db.ExternalEmployeeIdMappings.Add(mapping);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(ExternalEmployeeIdMapping), mapping.Id, $"Vendor id '{external}' mapped to employee {input.EmployeeId} on device {deviceId} of integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(mapping.Id);
    }

    public async Task<SaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken)
    {
        var mapping = await _db.ExternalEmployeeIdMappings.SingleOrDefaultAsync(m => m.Id == mappingId && m.DeviceId == deviceId && m.Device!.IntegrationId == integrationId, cancellationToken);
        if (mapping is null)
        {
            return SaveResult.Missing();
        }

        _db.ExternalEmployeeIdMappings.Remove(mapping);
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(ExternalEmployeeIdMapping), mapping.Id, $"Mapping of vendor id '{mapping.ExternalEmployeeId}' to employee {mapping.EmployeeId} removed from device {deviceId} of integration {integrationId}.");
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

        var queued = await QueueTimeZoneFailuresAsync(integrationId, "on request", cancellationToken, auditWhenNone: true);
        await _db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    public async Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new { DefaultTransport = i.Connections.Where(c => c.IsDefault).Select(c => (AttendanceTransport?)c.Transport).FirstOrDefault(), i.DefaultDeviceId })
            .SingleOrDefaultAsync(cancellationToken);
        if (integration is null)
        {
            return null;
        }

        var transport = integration.DefaultTransport ?? AttendanceTransport.FileUpload;
        var defaultDeviceInForce = transport == AttendanceTransport.FileUpload && integration.DefaultDeviceId is not null;
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
            Transport = transport.ToString(),
            TransportName = (await _catalog.ListTransportsAsync(cancellationToken))
                .Where(t => t.TransportKey == transport.ToString())
                .Select(t => t.DisplayName)
                .SingleOrDefault() ?? transport.ToString(),
        };
    }

    public async Task<PagedResult<RawNormalizedMappingItem>?> ListRawToNormalizedAsync(int integrationId, RawNormalizedFilter filter, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new { i.DisplayName, i.ConnectorKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (integration is null)
        {
            return null;
        }

        var vendorKey = (await _catalog.GetConnectorAsync(integration.ConnectorKey, cancellationToken))?.VendorKey ?? string.Empty;
        var size = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);

        // A direct read of raw and normalized attendance outside the engine: audited, and saved
        // before the read, so the read is on record even if it then fails.
        _audit.Record(
            AttendanceAuditActions.AttendanceDataRead,
            $"{nameof(RawAttendanceTransaction)},{nameof(NormalizedAttendanceTransaction)}",
            integrationId,
            $"Raw to normalized mapping of integration {integrationId} (batch {filter.BatchId?.ToString() ?? "any"}, status {filter.Status?.ToString() ?? "any"}, page {Math.Max(1, filter.Page)}, {size} per page).");
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
        var joined =
            from r in raws
            join n in _db.NormalizedAttendanceTransactions.AsNoTracking().Where(x => x.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded) on r.Id equals n.RawTransactionId into normalized
            from n in normalized.DefaultIfEmpty()
            select new { Raw = r, Normalized = n };

        // The total is counted on the joined rows, so it matches what the pages list; a page past the end is answered with the last page.
        var total = await joined.CountAsync(cancellationToken);
        var current = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)size)));

        var rows = await joined
            .OrderByDescending(x => x.Raw.Id)
            .ThenByDescending(x => x.Normalized!.Id)
            .Skip((current - 1) * size)
            .Take(size)
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

        var items = rows.Select(x =>
        {
            var n = x.Normalized;
            var employee = n?.EmployeeId is { } employeeId && employees.TryGetValue(employeeId, out var e) ? e : null;
            return new RawNormalizedMappingItem
            {
                VendorKey = vendorKey,
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

        return new PagedResult<RawNormalizedMappingItem> { Items = items, Total = total, Page = current, PageSize = size };
    }

    public async Task<VendorMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking()
            .Where(i => i.Id == integrationId)
            .Select(i => new { DefaultTransport = i.Connections.Where(c => c.IsDefault).Select(c => (AttendanceTransport?)c.Transport).FirstOrDefault(), i.ConnectorKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (integration is null)
        {
            return null;
        }

        var connector = await _catalog.GetConnectorAsync(integration.ConnectorKey, cancellationToken);
        var vendorKey = connector?.VendorKey ?? string.Empty;

        // What reads the integration's data: its connector's default payload format, else the connector's code adapter.
        if (PayloadFormatResolution.EffectiveFormatName(integration.DefaultTransport ?? AttendanceTransport.FileUpload, connector?.DefaultPayloadFormatName) is { } formatName
            && await _catalog.GetLatestFormatAsync(formatName, cancellationToken) is { } format)
        {
            return PayloadFormatLayout.Describe(format, vendorKey);
        }

        return (connector?.AdapterKey is { Length: > 0 } adapterKey ? _services.GetKeyedService<IAttendanceAdapter>(adapterKey) as IAttendanceMappingGuide : null)?.DescribeMapping()
            ?? new VendorMappingGuide { VendorKey = vendorKey, Summary = "Nothing describes how this integration's data is mapped: it has no payload format, and its connector's adapter does not describe its mapping." };
    }

    private static string Describe(AttendanceIntegration integration, AttendanceConnection connection) =>
        $"{integration.DisplayName}; {integration.Status}; {connection.Transport}/{connection.AuthMethod}; device {integration.DefaultDeviceId?.ToString() ?? "none"}; zone '{integration.TimeZoneId}'; cron '{connection.PollingSchedule}'; ref '{connection.SecretRef}'; settings {connection.Settings}";

    /// <summary>The usual authentication of a transport, for an input that names none.</summary>
    private static string DefaultAuthMethod(string transport) =>
        Enum.TryParse<AttendanceTransport>(transport, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? TransportDescriptors.Find(parsed)!.AuthMethods[0]
            : nameof(ConnectionAuthMethod.None);

    /// <summary>The integration form's connection fields as the default connection's input.</summary>
    private static ConnectionInput ToConnectionInput(IntegrationInput input) => new()
    {
        Name = DefaultConnectionName,
        Transport = input.Transport ?? string.Empty,
        AuthMethod = string.IsNullOrWhiteSpace(input.AuthMethod) ? DefaultAuthMethod(input.Transport ?? string.Empty) : input.AuthMethod.Trim(),
        Settings = input.Settings,
        SecretRef = input.ConnectionConfigRef,
        PollingSchedule = input.PollingScheduleCron,
        IsActive = true,
    };

    /// <summary>A connection name that no other connection of the integration has (ignoring case): the wanted one, else with a number.</summary>
    private static string UniqueConnectionName(AttendanceIntegration integration, string wanted)
    {
        var taken = integration.Connections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = wanted.Length > 255 ? wanted[..255] : wanted;
        for (var n = 2; taken.Contains(name); n++)
        {
            var suffix = $" {n}";
            name = (wanted.Length + suffix.Length > 255 ? wanted[..(255 - suffix.Length)] : wanted) + suffix;
        }

        return name;
    }

    private static void Apply(IntegrationInput input, AttendanceIntegration integration, AttendanceConnection connection)
    {
        var connectionInput = ToConnectionInput(input);
        var transport = Enum.Parse<AttendanceTransport>(connectionInput.Transport, ignoreCase: true);

        integration.DisplayName = input.DisplayName.Trim();
        integration.PayloadKeyRef = (input.PayloadKeyRef ?? string.Empty).Trim();
        integration.DefaultDeviceId = transport == AttendanceTransport.FileUpload ? input.DefaultDeviceId : null;
        integration.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? null : input.TimeZoneId.Trim();
        integration.Status = Enum.Parse<AttendanceIntegrationStatus>(input.Status, ignoreCase: true);

        ApplyConnection(connectionInput, connection);
    }

    private static void ApplyConnection(ConnectionInput connectionInput, AttendanceConnection connection)
    {
        var transport = Enum.Parse<AttendanceTransport>(connectionInput.Transport, ignoreCase: true);
        var method = Enum.Parse<ConnectionAuthMethod>(connectionInput.AuthMethod, ignoreCase: true);

        connection.Transport = transport;
        connection.AuthMethod = method;
        connection.Settings = ConnectionSettingsValidator.Serialize(connectionInput.Settings);

        // No authentication, no secret. Only a transport robeem pulls over has a schedule; an upload or a webhook is pushed to robeem.
        connection.SecretRef = method == ConnectionAuthMethod.None ? string.Empty : (connectionInput.SecretRef ?? string.Empty).Trim();
        connection.PollingSchedule = string.IsNullOrWhiteSpace(connectionInput.PollingSchedule) || !ConnectionSettingsValidator.IsPull(transport) ? null : connectionInput.PollingSchedule.Trim();
    }

    private static async Task<List<IntegrationListItem>> QueryItemsAsync(IRobeemDbContext db, ICatalogReader catalog, IQueryable<AttendanceIntegration> integrations, CancellationToken cancellationToken)
    {
        var rows = await integrations.Select(i => new
        {
            i.Id,
            i.DisplayName,
            i.ConnectorKey,
            i.DefaultDeviceId,
            DefaultDeviceName = i.DefaultDevice!.Name,
            i.TimeZoneId,
            i.Status,
            i.HealthStatus,
            i.LastSuccessfulSyncAt,
            Connection = i.Connections.Where(c => c.IsDefault).Select(c => new { c.Id, c.Transport, c.AuthMethod, c.SecretRef, c.PollingSchedule }).FirstOrDefault(),
            ConnectionCount = i.Connections.Count(),
            DeviceCount = db.AttendanceDevices.Count(d => d.IntegrationId == i.Id),
            MappingCount = db.ExternalEmployeeIdMappings.Count(m => m.Device!.IntegrationId == i.Id),
        }).ToListAsync(cancellationToken);

        // The connector and its vendor are the prime tenant's catalog: read by key, apart from the tenant's own query.
        var connectors = await catalog.GetConnectorsAsync(rows.Select(r => r.ConnectorKey), cancellationToken);

        // An integration with no default connection is a plain upload.
        return rows.Select(i => new IntegrationListItem
        {
            Id = i.Id,
            DisplayName = i.DisplayName,
            ConnectorKey = i.ConnectorKey,
            ConnectorName = connectors.TryGetValue(i.ConnectorKey, out var connector) ? connector.DisplayName : i.ConnectorKey,
            VendorKey = connector?.VendorKey ?? string.Empty,
            VendorName = connector?.VendorName ?? string.Empty,
            Transport = (i.Connection?.Transport ?? AttendanceTransport.FileUpload).ToString(),
            AuthMethod = (i.Connection?.AuthMethod ?? ConnectionAuthMethod.None).ToString(),
            DefaultConnectionId = i.Connection?.Id,
            ConnectionCount = i.ConnectionCount,
            PayloadFormatName = PayloadFormatResolution.EffectiveFormatName(
                i.Connection?.Transport ?? AttendanceTransport.FileUpload,
                connector?.DefaultPayloadFormatName),
            DefaultDeviceId = i.DefaultDeviceId,
            DefaultDeviceName = i.DefaultDeviceName,
            TimeZoneId = i.TimeZoneId,
            Status = i.Status.ToString(),
            HealthStatus = i.HealthStatus.ToString(),
            ConnectionConfigRef = i.Connection?.SecretRef ?? string.Empty,
            PollingScheduleCron = i.Connection?.PollingSchedule,
            LastSuccessfulSyncAt = i.LastSuccessfulSyncAt,
            DeviceCount = i.DeviceCount,
            MappingCount = i.MappingCount,
        }).ToList();
    }

    private static void ApplyDevice(DeviceInput input, AttendanceDevice device)
    {
        device.ExternalDeviceId = input.ExternalDeviceId.Trim();
        device.Name = input.Name.Trim();
        device.DeviceTypeName = input.DeviceTypeName.Trim();
        device.ConnectionId = input.ConnectionId;
        device.Model = (input.Model ?? string.Empty).Trim();
        device.SoftwareVersion = (input.SoftwareVersion ?? string.Empty).Trim();
        device.SdkVersion = (input.SdkVersion ?? string.Empty).Trim();
        device.TimeZoneId = input.TimeZoneId.Trim();
    }

    /// <summary>The connector's own transports first, then manual file upload, which any connector can be given (its files are read by a payload format).</summary>
    private static IReadOnlyList<TransportOption> SupportedTransportsOf(IEnumerable<string> connectorTransportKeys, IReadOnlyList<AttendanceTransportDefinition> definitions)
    {
        var own = connectorTransportKeys.ToHashSet(StringComparer.Ordinal);
        var fileUpload = nameof(AttendanceTransport.FileUpload);
        return definitions
            .Where(t => t.TransportKey == fileUpload || own.Contains(t.TransportKey))
            .OrderBy(t => t.TransportKey == fileUpload ? 1 : 0)
            .ThenBy(t => Enum.TryParse<AttendanceTransport>(t.TransportKey, out var transport) ? (int)transport : int.MaxValue)
            .Select(t => new TransportOption { Key = t.TransportKey, DisplayName = t.DisplayName, IsPull = t.IsPull, UsesPayloadFormat = t.UsesPayloadFormat, IsActive = t.IsActive })
            .ToList();
    }

    /// <summary>The decryption key of an encrypted payload is named by a reference, like the connection secret, and only an encrypted connector has one.</summary>
    private static void ValidatePayloadKey(IntegrationInput input, ConnectorInfo connector, Dictionary<string, string> errors)
    {
        var reference = (input.PayloadKeyRef ?? string.Empty).Trim();
        if (connector.EncryptionKind == PayloadEncryptionKind.None)
        {
            if (reference.Length > 0)
            {
                errors[nameof(IntegrationInput.PayloadKeyRef)] = "This connector's payload is not encrypted, so there is no key to name.";
            }
        }
        else if (reference.Length == 0)
        {
            errors[nameof(IntegrationInput.PayloadKeyRef)] = $"This connector's payload is encrypted ({connector.EncryptionKind}): name the secret that holds the key.";
        }
        else if (reference.Length > 255 || !ReferencePattern.IsMatch(reference))
        {
            errors[nameof(IntegrationInput.PayloadKeyRef)] = "Give the name the key is kept under (letters, digits and . _ : / -), never the key itself.";
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

        // The connection fields are the default connection's: held to what the transport declares, and the errors named by this form's fields.
        foreach (var (key, message) in ConnectionSettingsValidator.Validate(ToConnectionInput(input)))
        {
            var field = key switch
            {
                nameof(ConnectionInput.SecretRef) => nameof(IntegrationInput.ConnectionConfigRef),
                nameof(ConnectionInput.PollingSchedule) => nameof(IntegrationInput.PollingScheduleCron),
                nameof(ConnectionInput.Name) => null,
                _ => key,
            };
            if (field is not null)
            {
                errors[field] = message;
            }
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

    /// <summary>
    /// Sets the integration's raw punches that failed for want of a time zone back to Pending; the caller saves. Only such a punch: one
    /// rejected at ingestion (an unparseable timestamp, a payload the sanitizer refused) would fail again. Status and error are the only
    /// columns of a raw row that may change (invariant 2); the normalization job picks the rows up.
    /// </summary>
    private async Task<int> QueueTimeZoneFailuresAsync(int integrationId, string because, CancellationToken cancellationToken, bool auditWhenNone = false)
    {
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

        if (failed.Count > 0 || auditWhenNone)
        {
            _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceIntegration), integrationId, $"{failed.Count} punch(es) that failed for want of a time zone were queued to be normalized again ({because}).");
        }

        return failed.Count;
    }

    /// <summary>The connector with this key, or null with an error named after the field when there is none. A connector that can't be used is returned too, with the reason as the error.</summary>
    private async Task<ConnectorInfo?> FindUsableConnectorAsync(string connectorKey, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        var connector = connectorKey.Length == 0 ? null : await _catalog.GetConnectorAsync(connectorKey, cancellationToken);
        if (connector is null)
        {
            errors[nameof(IntegrationInput.ConnectorKey)] = "The selected connector does not exist.";
        }
        else if (!connector.IsUsable)
        {
            errors[nameof(IntegrationInput.ConnectorKey)] = !connector.IsActive || !connector.VendorIsActive
                ? "This connector is not active (or its vendor is not), so it can't be used."
                : connector.EncryptionKind != PayloadEncryptionKind.None
                    ? $"This server can't decrypt the {connector.EncryptionKind} payload this connector delivers."
                    : "This connector has neither a default payload format nor a code adapter, so nothing can read what it delivers.";
        }

        return connector;
    }

    private async Task<IReadOnlyList<DeviceItem>> QueryDevicesAsync(int? integrationId, CancellationToken cancellationToken)
    {
        var devices = await _db.AttendanceDevices
            .AsNoTracking()
            .Include(d => d.Integration)
            .Include(d => d.Connection)
            .Where(d => integrationId == null || d.IntegrationId == integrationId)
            .OrderBy(d => d.Integration!.DisplayName)
            .ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);
        var defaultConnectionNames = await _db.AttendanceConnections
            .AsNoTracking()
            .Where(c => c.IsDefault && (integrationId == null || c.IntegrationId == integrationId))
            .ToDictionaryAsync(c => c.IntegrationId, c => c.Name, cancellationToken);
        var mappingCounts = await _db.ExternalEmployeeIdMappings
            .AsNoTracking()
            .Where(m => integrationId == null || m.Device!.IntegrationId == integrationId)
            .GroupBy(m => m.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.DeviceId, g => g.Count, cancellationToken);
        return devices
            .Select(d => new DeviceItem
            {
                Id = d.Id,
                IntegrationId = d.IntegrationId,
                IntegrationName = d.Integration?.DisplayName ?? string.Empty,
                MappingCount = mappingCounts.GetValueOrDefault(d.Id),
                ExternalDeviceId = d.ExternalDeviceId,
                Name = d.Name,
                ConnectionId = d.ConnectionId,
                ConnectionName = d.Connection?.Name ?? defaultConnectionNames.GetValueOrDefault(d.IntegrationId, string.Empty),
                DeviceType = d.DeviceTypeName ?? string.Empty,
                Model = d.Model,
                SoftwareVersion = d.SoftwareVersion,
                SdkVersion = d.SdkVersion,
                TimeZoneId = d.TimeZoneId,
                LastSeenAt = d.LastSeenAt,
            })
            .ToList();
    }

    /// <summary>Sets each item's transport display name from the transport definitions.</summary>
    private async Task NameTransportsAsync(IReadOnlyCollection<IntegrationListItem> items, CancellationToken cancellationToken)
    {
        var names = (await _catalog.ListTransportsAsync(cancellationToken)).ToDictionary(t => t.TransportKey, t => t.DisplayName, StringComparer.Ordinal);
        foreach (var item in items)
        {
            item.TransportName = names.TryGetValue(item.Transport, out var name) ? name : item.Transport;
        }
    }

    /// <summary>
    /// The chosen transport must be defined, supported by the connector (or be file upload), and available: a
    /// retired one can't be chosen, but an integration that already has it keeps it.
    /// </summary>
    private async Task ValidateTransportAsync(IntegrationInput input, ConnectorInfo connector, string? currentTransport, Dictionary<string, string> errors, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport) || !Enum.IsDefined(transport))
        {
            errors[nameof(IntegrationInput.Transport)] = $"Choose how the integration receives attendance: {string.Join(", ", Enum.GetNames<AttendanceTransport>())}.";
            return;
        }

        var definitions = await _catalog.ListTransportsAsync(cancellationToken);
        var supported = SupportedTransportsOf(connector.Transports, definitions);
        var chosen = supported.FirstOrDefault(t => t.Key == transport.ToString());
        if (chosen is null)
        {
            errors[nameof(IntegrationInput.Transport)] = $"{connector.DisplayName} can't receive attendance by {transport}. It supports: {(supported.Count == 0 ? "nothing yet" : string.Join(", ", supported.Select(t => t.DisplayName)))}.";
        }
        else if (!chosen.IsActive && chosen.Key != currentTransport)
        {
            errors[nameof(IntegrationInput.Transport)] = $"{chosen.DisplayName} is not available for new integrations.";
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

    private async Task<Dictionary<string, string>> ValidateDeviceAsync(int integrationId, int? existingId, string? currentTypeName, DeviceInput input, CancellationToken cancellationToken)
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

        // A device is reached through one of its own integration's connections, or the integration's default.
        if (input.ConnectionId is { } connectionId
            && !await _db.AttendanceConnections.AnyAsync(c => c.Id == connectionId && c.IntegrationId == integrationId, cancellationToken))
        {
            errors[nameof(DeviceInput.ConnectionId)] = "Choose one of this integration's own connections, or the default.";
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 255)
        {
            errors[nameof(DeviceInput.Name)] = "A name is required (at most 255 characters).";
        }

        // A device may keep a type that has since been retired; a new choice must be an active one.
        // The type is the catalog's, referred to by name: matched ignoring case, and kept as the catalog spells it.
        var typeName = (input.DeviceTypeName ?? string.Empty).Trim();
        var types = await _catalog.ListDeviceTypesAsync(cancellationToken);
        var type = types.FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (type is null || !(type.IsActive || type.Name == currentTypeName))
        {
            errors[nameof(DeviceInput.DeviceTypeName)] = "Choose one of the active device types.";
        }
        else
        {
            input.DeviceTypeName = type.Name;
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
