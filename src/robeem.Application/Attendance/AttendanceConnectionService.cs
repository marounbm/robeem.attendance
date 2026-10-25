using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Common;
using robeem.Application.Persistence;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class AttendanceConnectionService : IAttendanceConnectionService
{
    // How long a test waits for the other end. A test must not hang a request on a host that never answers.
    private static readonly TimeSpan DefaultTestTimeout = TimeSpan.FromSeconds(15);

    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly ISecretStore _secrets;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _testTimeout;

    public AttendanceConnectionService(IRobeemDbContext db, IAttendanceAuditTrail audit, ISecretStore secrets, IServiceProvider services, TimeProvider clock, TimeSpan? testTimeout = null)
    {
        _db = db;
        _audit = audit;
        _secrets = secrets;
        _services = services;
        _clock = clock;
        _testTimeout = testTimeout ?? DefaultTestTimeout;
    }

    public IReadOnlyList<TransportDescriptorItem> GetDescriptors() => TransportDescriptors.All;

    public async Task<IReadOnlyList<ConnectionListItem>?> ListAsync(int integrationId, CancellationToken cancellationToken)
    {
        if (!await _db.AttendanceIntegrations.AnyAsync(i => i.Id == integrationId, cancellationToken))
        {
            return null;
        }

        var connections = await _db.AttendanceConnections.AsNoTracking()
            .Where(c => c.IntegrationId == integrationId)
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var deviceCounts = await _db.AttendanceDevices.AsNoTracking()
            .Where(d => d.IntegrationId == integrationId && d.ConnectionId != null)
            .GroupBy(d => d.ConnectionId!.Value)
            .Select(g => new { ConnectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ConnectionId, g => g.Count, cancellationToken);
        var names = await _db.AttendanceTransportDefinitions.AsNoTracking().ToDictionaryAsync(t => t.TransportKey, t => t.DisplayName, StringComparer.Ordinal, cancellationToken);

        return connections.Select(c => new ConnectionListItem
        {
            Id = c.Id,
            IntegrationId = c.IntegrationId,
            Name = c.Name,
            IsDefault = c.IsDefault,
            Transport = c.Transport.ToString(),
            TransportName = names.GetValueOrDefault(c.Transport.ToString(), c.Transport.ToString()),
            AuthMethod = c.AuthMethod.ToString(),
            Settings = ConnectionSettingsValidator.Parse(c.Settings),
            SecretRef = c.SecretRef,
            PollingSchedule = c.PollingSchedule,
            IsActive = c.IsActive,
            DeviceCount = deviceCounts.GetValueOrDefault(c.Id),
            LastTestedAt = c.LastTestedAt,
            LastTestSucceeded = c.LastTestSucceeded,
            LastTestMessage = c.LastTestMessage,
        }).ToList();
    }

    public async Task<ConnectionInput?> GetInputAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        var c = await _db.AttendanceConnections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == connectionId && x.IntegrationId == integrationId, cancellationToken);
        return c is null
            ? null
            : new ConnectionInput
            {
                Name = c.Name,
                Transport = c.Transport.ToString(),
                AuthMethod = c.AuthMethod.ToString(),
                Settings = ConnectionSettingsValidator.Parse(c.Settings),
                SecretRef = c.SecretRef,
                PollingSchedule = c.PollingSchedule,
                IsActive = c.IsActive,
            };
    }

    public async Task<SaveResult> CreateAsync(int integrationId, ConnectionInput input, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations.AsNoTracking().Include(i => i.Connector).SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return SaveResult.Missing();
        }

        var errors = ConnectionSettingsValidator.Validate(input);
        await ValidateNameAndTransportAsync(integration, input, null, null, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        // The first connection of an integration is its default; later ones become default only by being made so.
        var hasDefault = await _db.AttendanceConnections.AnyAsync(c => c.IntegrationId == integrationId && c.IsDefault, cancellationToken);
        var connection = new AttendanceConnection { IntegrationId = integrationId, IsDefault = !hasDefault };
        Apply(input, connection);
        _db.AttendanceConnections.Add(connection);
        await _db.SaveChangesAsync(cancellationToken);

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnection), connection.Id, $"Connection '{connection.Name}' ({connection.Transport}/{connection.AuthMethod}) added to integration {integrationId}; secret reference '{connection.SecretRef}'.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(connection.Id);
    }

    public async Task<SaveResult> UpdateAsync(int integrationId, int connectionId, ConnectionInput input, CancellationToken cancellationToken)
    {
        var connection = await _db.AttendanceConnections.SingleOrDefaultAsync(c => c.Id == connectionId && c.IntegrationId == integrationId, cancellationToken);
        if (connection is null)
        {
            return SaveResult.Missing();
        }

        var integration = await _db.AttendanceIntegrations.AsNoTracking().Include(i => i.Connector).SingleAsync(i => i.Id == integrationId, cancellationToken);
        var errors = ConnectionSettingsValidator.Validate(input);
        await ValidateNameAndTransportAsync(integration, input, connection.Id, connection.Transport, errors, cancellationToken);
        if (errors.Count > 0)
        {
            return SaveResult.Invalid(errors);
        }

        var before = $"{connection.Name} ({connection.Transport}/{connection.AuthMethod}); {(connection.IsActive ? "active" : "inactive")}; cron '{connection.PollingSchedule}'; ref '{connection.SecretRef}'; settings {connection.Settings}";
        var transportChanged = connection.Transport != Enum.Parse<AttendanceTransport>(input.Transport, ignoreCase: true);
        Apply(input, connection);

        // The test result described the old endpoint.
        if (transportChanged)
        {
            connection.LastTestedAt = null;
            connection.LastTestSucceeded = null;
            connection.LastTestMessage = null;
        }

        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnection), connection.Id, $"Connection changed. Before: {before}. After: {connection.Name} ({connection.Transport}/{connection.AuthMethod}); {(connection.IsActive ? "active" : "inactive")}; cron '{connection.PollingSchedule}'; ref '{connection.SecretRef}'; settings {connection.Settings}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(connection.Id);
    }

    public async Task<SaveResult> MakeDefaultAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        var connections = await _db.AttendanceConnections.Where(c => c.IntegrationId == integrationId).ToListAsync(cancellationToken);
        var target = connections.FirstOrDefault(c => c.Id == connectionId);
        if (target is null)
        {
            return SaveResult.Missing();
        }

        if (target.IsDefault)
        {
            return SaveResult.Success(target.Id);
        }

        if (!target.IsActive)
        {
            return SaveResult.Invalid("id", "A connection that is switched off can't be the default. Switch it on first.");
        }

        // Two saves: the database allows one default per integration, so the old one is released before the new one is set.
        foreach (var current in connections.Where(c => c.IsDefault))
        {
            current.IsDefault = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
        target.IsDefault = true;
        _audit.Record(AttendanceAuditActions.IntegrationChanged, nameof(AttendanceConnection), target.Id, $"Connection '{target.Name}' made the default of integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);
        return SaveResult.Success(target.Id);
    }

    public async Task<ConnectionTestResult?> TestAsync(int integrationId, int connectionId, CancellationToken cancellationToken)
    {
        var connection = await _db.AttendanceConnections
            .Include(c => c.Integration).ThenInclude(i => i!.Connector).ThenInclude(c => c!.Vendor)
            .SingleOrDefaultAsync(c => c.Id == connectionId && c.IntegrationId == integrationId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var (succeeded, message) = await RunTestAsync(connection, cancellationToken);
        var testedAt = _clock.GetLocalNow().DateTime;
        connection.LastTestedAt = testedAt;
        connection.LastTestSucceeded = succeeded;
        connection.LastTestMessage = message;

        // Whether it worked, and our own wording: what the other end answered is never recorded.
        _audit.Record(AttendanceAuditActions.ConnectionTested, nameof(AttendanceConnection), connection.Id, $"Connection '{connection.Name}' ({connection.Transport}) of integration {integrationId} tested: {(succeeded ? "succeeded" : "failed")}. {message}");
        await _db.SaveChangesAsync(cancellationToken);
        return new ConnectionTestResult { Succeeded = succeeded, Message = message, TestedAt = testedAt };
    }

    private static void Apply(ConnectionInput input, AttendanceConnection connection)
    {
        var method = Enum.Parse<ConnectionAuthMethod>(input.AuthMethod, ignoreCase: true);
        var transport = Enum.Parse<AttendanceTransport>(input.Transport, ignoreCase: true);
        connection.Name = input.Name.Trim();
        connection.Transport = transport;
        connection.AuthMethod = method;
        connection.Settings = ConnectionSettingsValidator.Serialize(input.Settings);

        // No authentication, no secret. Only a transport robeem pulls over has a schedule.
        connection.SecretRef = method == ConnectionAuthMethod.None ? string.Empty : (input.SecretRef ?? string.Empty).Trim();
        connection.PollingSchedule = string.IsNullOrWhiteSpace(input.PollingSchedule) || !ConnectionSettingsValidator.IsPull(transport) ? null : input.PollingSchedule.Trim();
        connection.IsActive = input.IsActive;
    }

    /// <summary>One of the fixed outcomes of a test, never built from an exception or a response.</summary>
    private static (bool Succeeded, string Message) Fail(string message) => (false, message);

    private async Task ValidateNameAndTransportAsync(
        AttendanceIntegration integration,
        ConnectionInput input,
        int? existingId,
        AttendanceTransport? currentTransport,
        Dictionary<string, string> errors,
        CancellationToken cancellationToken)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length > 0 && await _db.AttendanceConnections.AnyAsync(c => c.IntegrationId == integration.Id && c.Name == name && c.Id != existingId, cancellationToken))
        {
            errors[nameof(ConnectionInput.Name)] = "Another connection of this integration has this name.";
        }

        // The transport must be one the integration's connector offers (or file upload, which every connector has).
        if (Enum.TryParse<AttendanceTransport>(input.Transport, ignoreCase: true, out var transport) && Enum.IsDefined(transport) && transport != currentTransport)
        {
            var key = transport.ToString();
            var definition = await _db.AttendanceTransportDefinitions.AsNoTracking().SingleOrDefaultAsync(t => t.TransportKey == key, cancellationToken);
            var offered = transport == AttendanceTransport.FileUpload
                || (definition is not null && await _db.AttendanceConnectorTransports.AnyAsync(t => t.ConnectorId == integration.ConnectorId && t.TransportId == definition.Id, cancellationToken));
            if (!offered)
            {
                errors[nameof(ConnectionInput.Transport)] = $"{integration.Connector!.DisplayName} can't receive attendance by {transport}.";
            }
            else if (definition is { IsActive: false })
            {
                errors[nameof(ConnectionInput.Transport)] = $"{definition.DisplayName} is not available for new connections.";
            }
        }
    }

    private async Task<(bool Succeeded, string Message)> RunTestAsync(AttendanceConnection connection, CancellationToken cancellationToken)
    {
        var input = new ConnectionInput
        {
            Name = connection.Name,
            Transport = connection.Transport.ToString(),
            AuthMethod = connection.AuthMethod.ToString(),
            Settings = ConnectionSettingsValidator.Parse(connection.Settings),
            SecretRef = connection.SecretRef,
            PollingSchedule = connection.PollingSchedule,
        };
        if (ConnectionSettingsValidator.Validate(input).Count > 0)
        {
            return Fail("The connection's settings are incomplete or not valid. Edit it and save it first.");
        }

        if (connection.Transport == AttendanceTransport.FileUpload)
        {
            return (true, "Nothing to test: people upload the files.");
        }

        var secret = connection.AuthMethod == ConnectionAuthMethod.None ? null : await _secrets.GetSecretAsync(connection.SecretRef, cancellationToken);
        if (connection.AuthMethod != ConnectionAuthMethod.None && string.IsNullOrEmpty(secret))
        {
            return Fail($"The secret '{connection.SecretRef}' is not configured on the server.");
        }

        if (connection.Transport == AttendanceTransport.Webhook)
        {
            return (true, "Ready: the secret is configured, and the sender is checked by it.");
        }

        if (connection.Transport != AttendanceTransport.RestApi)
        {
            return Fail($"No adapter reads {connection.Transport} connections yet, so this one can't be tested.");
        }

        var connector = connection.Integration!.Connector!;
        var adapter = connector.AdapterKey is { Length: > 0 } adapterKey ? _services.GetKeyedService<IAttendanceAdapter>(adapterKey) : null;
        if (adapter is null)
        {
            return Fail($"No adapter is registered for connector '{connector.ConnectorKey}', so this connection can't be tested.");
        }

        var config = new AttendanceIntegrationConfig(connection.IntegrationId, connector.Vendor!.VendorKey, connection.SecretRef)
        {
            ConnectionId = connection.Id,
            Settings = ConnectionSettingsValidator.Parse(connection.Settings),
            ConnectionSecret = secret,
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_testTimeout);
        try
        {
            var result = await adapter.ValidateConnectionAsync(config, timeout.Token);
            return result.IsValid ? (true, "The connection works.") : Fail("The other end refused the connection, or the credential was not accepted.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail($"The other end did not answer within {_testTimeout.TotalSeconds:0.#} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail("The other end could not be reached.");
        }
    }
}
