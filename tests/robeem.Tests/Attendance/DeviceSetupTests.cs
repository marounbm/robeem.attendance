using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Setting a device up from the vendor down: the connector's integration is found or made, so a person never names it. Everything made
/// is saved together, and a refused device leaves nothing behind.
/// </summary>
public sealed class DeviceSetupTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public DeviceSetupTests()
    {
        _database.SeedAttendanceCatalog();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task TheFirstDeviceOfAConnector_MakesItsIntegration_WithTheConnectionAsItsDefault()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var setup = await Service(db).SetUpDeviceAsync(FileUpload("zkteco", "DEV-1", "Front door"), CancellationToken.None);

        Assert.True(setup.Result.Succeeded, string.Join(" ", setup.Result.Errors.Values));
        Assert.True(setup.IntegrationCreated);
        Assert.False(setup.ConnectionCreated);
        var integration = db.AttendanceIntegrations.Single();
        Assert.Equal(("zkteco", "ZKTeco", AttendanceIntegrationStatus.Active), (integration.ConnectorKey, integration.DisplayName, integration.Status));
        var connection = db.AttendanceConnections.Single();
        Assert.Equal((integration.Id, true, AttendanceTransport.FileUpload), (connection.IntegrationId, connection.IsDefault, connection.Transport));
        var device = db.AttendanceDevices.Single();
        Assert.Equal((setup.Result.Id, integration.Id, "DEV-1"), (device.Id, device.IntegrationId, device.ExternalDeviceId));
        Assert.Null(device.ConnectionId);
        Assert.Equal(setup.IntegrationId, integration.Id);
    }

    [Fact]
    public async Task TheNextDevice_JoinsTheSameIntegration_AndSharesAnExistingConnection()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.SetUpDeviceAsync(FileUpload("zkteco", "DEV-1", "Front door"), CancellationToken.None);
        var shared = db.AttendanceConnections.Single().Id;

        var second = FileUpload("zkteco", "DEV-2", "Back door");
        second.ConnectionId = shared;
        var setup = await service.SetUpDeviceAsync(second, CancellationToken.None);

        Assert.True(setup.Result.Succeeded, string.Join(" ", setup.Result.Errors.Values));
        Assert.False(setup.IntegrationCreated);
        Assert.False(setup.ConnectionCreated);
        Assert.Single(db.AttendanceIntegrations);
        Assert.Single(db.AttendanceConnections);
        Assert.Equal([shared, shared], db.AttendanceDevices.OrderBy(d => d.Id).Select(d => d.ConnectionId ?? shared));
        Assert.Equal(2, db.AttendanceDevices.Count(d => d.IntegrationId == setup.IntegrationId));
    }

    [Fact]
    public async Task ANewConnectionOnAnExistingIntegration_IsTheDevicesOwn_AndNotTheDefault()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.SetUpDeviceAsync(FileUpload("suprema-biostar", "DEV-1", "Front door"), CancellationToken.None);

        var rest = FileUpload("suprema-biostar", "DEV-2", "Warehouse");
        rest.Transport = "RestApi";
        rest.AuthMethod = "ApiKey";
        rest.ConnectionConfigRef = "warehouse-biostar-key";
        rest.PollingScheduleCron = "*/15 * * * *";
        var setup = await service.SetUpDeviceAsync(rest, CancellationToken.None);

        Assert.True(setup.Result.Succeeded, string.Join(" ", setup.Result.Errors.Values));
        Assert.False(setup.IntegrationCreated);
        Assert.True(setup.ConnectionCreated);
        Assert.Single(db.AttendanceIntegrations);
        var made = db.AttendanceConnections.Single(c => c.Transport == AttendanceTransport.RestApi);
        Assert.Equal((false, ConnectionAuthMethod.ApiKey, "warehouse-biostar-key", "*/15 * * * *", "Warehouse connection"), (made.IsDefault, made.AuthMethod, made.SecretRef, made.PollingSchedule, made.Name));
        Assert.Equal(made.Id, db.AttendanceDevices.Single(d => d.ExternalDeviceId == "DEV-2").ConnectionId);
        Assert.Null(db.AttendanceDevices.Single(d => d.ExternalDeviceId == "DEV-1").ConnectionId);
    }

    [Fact]
    public async Task ARefusedDevice_LeavesNoIntegrationAndNoConnectionBehind()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var input = FileUpload("zkteco", "DEV-1", "Front door");
        input.Device.DeviceTypeName = "Not a type";
        input.Device.TimeZoneId = "Mars/Olympus";

        var setup = await Service(db).SetUpDeviceAsync(input, CancellationToken.None);

        Assert.False(setup.Result.Succeeded);
        Assert.Contains("Device.DeviceTypeName", setup.Result.Errors.Keys);
        Assert.Contains("Device.TimeZoneId", setup.Result.Errors.Keys);
        Assert.Empty(db.AttendanceIntegrations);
        Assert.Empty(db.AttendanceConnections);
        Assert.Empty(db.AttendanceDevices);
    }

    [Fact]
    public async Task ADeviceIdAlreadyOnTheIntegration_IsRefused_AndNoConnectionIsMadeForIt()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.SetUpDeviceAsync(FileUpload("suprema-biostar", "DEV-1", "Front door"), CancellationToken.None);

        var again = FileUpload("suprema-biostar", "DEV-1", "Same id");
        again.Transport = "RestApi";
        again.AuthMethod = "ApiKey";
        again.ConnectionConfigRef = "another-key";
        var setup = await service.SetUpDeviceAsync(again, CancellationToken.None);

        Assert.Contains("Device.ExternalDeviceId", setup.Result.Errors.Keys);
        Assert.Single(db.AttendanceConnections);
        Assert.Single(db.AttendanceDevices);
    }

    [Fact]
    public async Task AConnectorThatCanNotBeUsed_OrIsUnknown_IsRefusedOnTheConnectorField()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        // The webhook connector has no payload format and no adapter registered in this test.
        var unusable = await service.SetUpDeviceAsync(FileUpload("generic-webhook", "DEV-1", "Door"), CancellationToken.None);
        var unknown = await service.SetUpDeviceAsync(FileUpload("no-such-connector", "DEV-1", "Door"), CancellationToken.None);

        Assert.Contains("ConnectorKey", unusable.Result.Errors.Keys);
        Assert.Contains("ConnectorKey", unknown.Result.Errors.Keys);
        Assert.Empty(db.AttendanceIntegrations);
    }

    [Fact]
    public async Task AConnectionThatIsNotTheConnectorsOwn_IsRefused()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);
        await service.SetUpDeviceAsync(FileUpload("zkteco", "DEV-1", "Front door"), CancellationToken.None);
        var zkConnection = db.AttendanceConnections.Single().Id;

        var other = FileUpload("generic-csv", "DEV-9", "Other");
        other.ConnectionId = zkConnection;
        var setup = await service.SetUpDeviceAsync(other, CancellationToken.None);

        Assert.Contains("ConnectionId", setup.Result.Errors.Keys);
        Assert.Single(db.AttendanceIntegrations);
        Assert.Single(db.AttendanceDevices);
    }

    [Fact]
    public async Task ATransportTheConnectorDoesNotSupport_OrASecretThatIsNotAReference_IsRefusedLikeOnTheIntegrationForm()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = Service(db);

        var noRest = FileUpload("zkteco", "DEV-1", "Door");
        noRest.Transport = "RestApi";
        noRest.AuthMethod = "ApiKey";
        noRest.ConnectionConfigRef = "key";
        var typedSecret = FileUpload("suprema-biostar", "DEV-1", "Door");
        typedSecret.Transport = "RestApi";
        typedSecret.AuthMethod = "ApiKey";
        typedSecret.ConnectionConfigRef = "my secret with spaces!";

        Assert.Contains("Transport", (await service.SetUpDeviceAsync(noRest, CancellationToken.None)).Result.Errors.Keys);
        Assert.Contains("ConnectionConfigRef", (await service.SetUpDeviceAsync(typedSecret, CancellationToken.None)).Result.Errors.Keys);
        Assert.Empty(db.AttendanceIntegrations);
    }

    [Fact]
    public async Task EverythingMadeIsAudited()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await Service(db).SetUpDeviceAsync(FileUpload("zkteco", "DEV-1", "Front door"), CancellationToken.None);

        var entries = db.AttendanceAuditLogs.Select(a => a.TargetType).ToList();
        Assert.Contains(nameof(AttendanceIntegration), entries);
        Assert.Contains(nameof(AttendanceDevice), entries);
    }

    private static DeviceSetupInput FileUpload(string connectorKey, string deviceId, string name) => new()
    {
        ConnectorKey = connectorKey,
        Transport = "FileUpload",
        Device = new DeviceInput { ExternalDeviceId = deviceId, Name = name, DeviceTypeName = "Biometric", TimeZoneId = "Asia/Beirut" },
    };

    private AttendanceIntegrationService Service(RobeemDbContext db) =>
        new(db, _database.CreateCatalogReader(new StubAdapterCatalog("suprema-biostar")), new NoSync(), new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), _services);

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
