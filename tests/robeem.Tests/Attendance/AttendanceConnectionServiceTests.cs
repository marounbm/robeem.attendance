using System.Text;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The connections of an integration: adding, the one default, devices that name one, and the test of a connection.</summary>
public sealed class AttendanceConnectionServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeAdapter _adapter = new();
    private readonly int _integrationId;
    private readonly int _otherIntegrationId;

    public AttendanceConnectionServiceTests()
    {
        _database.SeedAttendanceCatalog();
        using var db = _database.CreateContext(TestDatabase.TenantA);

        // BioStar offers its REST API; for these tests it takes pushed events too.
        var biostarConnector = AttendanceTestData.Seeded(db, "suprema-biostar");
        biostarConnector.Transports.Add(new AttendanceConnectorTransport { Transport = db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Webhook") });
        var biostar = new AttendanceIntegration { Connector = biostarConnector, DisplayName = "BioStar" }.WithConnection(AttendanceTransport.RestApi, "biostar-secret");
        var other = new AttendanceIntegration { Connector = AttendanceTestData.Seeded(db, "generic-csv"), DisplayName = "Uploads" }.WithConnection();
        db.AddRange(biostar, other);
        db.SaveChanges();
        (_integrationId, _otherIntegrationId) = (biostar.Id, other.Id);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AConnectionIsAddedToAnIntegration_NotTheDefault_AndListedAfterIt()
    {
        var created = await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None));

        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));
        var list = (await Run(s => s.ListAsync(_integrationId, CancellationToken.None)))!;
        Assert.Equal(["Default", "Push"], list.Select(c => c.Name));
        Assert.Equal((true, false), (list[0].IsDefault, list[1].IsDefault));
        Assert.Equal(("Webhook", "HmacSignature", "push-secret"), (list[1].Transport, list[1].AuthMethod, list[1].SecretRef));
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.IntegrationChanged && a.TargetType == nameof(AttendanceConnection) && a.TargetId == created.Id);
    }

    [Fact]
    public async Task TheNameIsUniqueWithinTheIntegration_AndTheTransportMustBeOneTheConnectorOffers()
    {
        await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None));

        var duplicate = await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None));
        var sameNameElsewhere = await Run(s => s.CreateAsync(_otherIntegrationId, new ConnectionInput { Name = "Push", Transport = "FileUpload", AuthMethod = "None" }, CancellationToken.None));
        var notOffered = await Run(s => s.CreateAsync(_integrationId, new ConnectionInput { Name = "Drop", Transport = "Sftp", AuthMethod = "SshKey", Settings = { ["host"] = "h", ["remotePath"] = "/", ["hostKeyFingerprint"] = "SHA256:x" } }, CancellationToken.None));

        Assert.Contains(nameof(ConnectionInput.Name), duplicate.Errors.Keys);
        Assert.True(sameNameElsewhere.Succeeded);
        Assert.Contains("can't receive attendance by Sftp", notOffered.Errors[nameof(ConnectionInput.Transport)]);
    }

    [Fact]
    public async Task ADifferentConnectionCanBeMadeTheDefault_TheOldOneIsReleased_AndASwitchedOffOneCanNot()
    {
        var push = (await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None))).Id!.Value;
        var off = Webhook("Off");
        off.IsActive = false;
        var offId = (await Run(s => s.CreateAsync(_integrationId, off, CancellationToken.None))).Id!.Value;

        var refused = await Run(s => s.MakeDefaultAsync(_integrationId, offId, CancellationToken.None));
        var made = await Run(s => s.MakeDefaultAsync(_integrationId, push, CancellationToken.None));
        var elsewhere = await Run(s => s.MakeDefaultAsync(_otherIntegrationId, push, CancellationToken.None));

        Assert.Contains("id", refused.Errors.Keys);
        Assert.True(made.Succeeded);
        Assert.True(elsewhere.NotFound);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["Push"], db.AttendanceConnections.Where(c => c.IntegrationId == _integrationId && c.IsDefault).Select(c => c.Name));
    }

    [Fact]
    public async Task TheDatabaseAllowsOneDefaultPerIntegration()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        db.AttendanceConnections.Add(new AttendanceConnection { IntegrationId = _integrationId, Name = "Second default", IsDefault = true });

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AConnectionIsUpdated_AndChangingItsTransportClearsTheOldTestResult()
    {
        var id = (await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None))).Id!.Value;
        _secrets.Set("push-secret", "s3cret");
        await Run(s => s.TestAsync(_integrationId, id, CancellationToken.None));

        var changed = Webhook("Push renamed");
        changed.AuthMethod = "ApiKey";
        var updated = await Run(s => s.UpdateAsync(_integrationId, id, changed, CancellationToken.None));
        var retyped = await Run(s => s.UpdateAsync(_integrationId, id, new ConnectionInput { Name = "Push renamed", Transport = "FileUpload", AuthMethod = "None" }, CancellationToken.None));

        Assert.True(updated.Succeeded);
        Assert.True(retyped.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var connection = db.AttendanceConnections.Single(c => c.Id == id);
        Assert.Equal(("Push renamed", AttendanceTransport.FileUpload, ConnectionAuthMethod.None, string.Empty), (connection.Name, connection.Transport, connection.AuthMethod, connection.SecretRef));
        Assert.Null(connection.LastTestedAt);
    }

    [Fact]
    public async Task ADeviceMayNameAConnectionOfItsOwnIntegrationOnly()
    {
        var push = (await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None))).Id!.Value;
        var foreign = (await Run(s => s.CreateAsync(_otherIntegrationId, new ConnectionInput { Name = "Elsewhere", Transport = "FileUpload", AuthMethod = "None" }, CancellationToken.None))).Id!.Value;
        int typeId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            typeId = db.AttendanceDeviceTypes.First().Id;
        }

        using var context = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceIntegrationService(context, null!, new AttendanceAuditTrail(context, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog());
        DeviceInput Device(string id, int? connection) => new() { ExternalDeviceId = id, Name = id, DeviceTypeId = typeId, TimeZoneId = "UTC", ConnectionId = connection };

        var ok = await service.AddDeviceAsync(_integrationId, Device("D1", push), CancellationToken.None);
        var refused = await service.AddDeviceAsync(_integrationId, Device("D2", foreign), CancellationToken.None);
        var defaultOne = await service.AddDeviceAsync(_integrationId, Device("D3", null), CancellationToken.None);

        Assert.True(ok.Succeeded);
        Assert.Contains(nameof(DeviceInput.ConnectionId), refused.Errors.Keys);
        Assert.True(defaultOne.Succeeded);
        var devices = (await service.ListDevicesAsync(_integrationId, CancellationToken.None))!;
        var named = devices.Single(d => d.ExternalDeviceId == "D1");
        var unnamed = devices.Single(d => d.ExternalDeviceId == "D3");
        Assert.Equal(("Push", push), (named.ConnectionName, named.ConnectionId));
        Assert.Equal("Default", unnamed.ConnectionName);
        Assert.Null(unnamed.ConnectionId);
    }

    [Fact]
    public async Task ATestOfAFileUploadHasNothingToTest_AndIsRecordedAndAudited()
    {
        var upload = await UploadConnectionIdAsync();

        var result = (await Run(s => s.TestAsync(_otherIntegrationId, upload, CancellationToken.None)))!;

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var connection = db.AttendanceConnections.Single(c => c.Id == upload);
        Assert.Equal((true, result.Message), (connection.LastTestSucceeded, connection.LastTestMessage));
        Assert.NotNull(connection.LastTestedAt);
        Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.ConnectionTested && a.TargetId == upload);
    }

    [Fact]
    public async Task ATestWithoutItsSecret_FailsAndNamesTheMissingSecret_NotItsValue()
    {
        var defaultId = await DefaultConnectionIdAsync(_integrationId);

        var result = (await Run(s => s.TestAsync(_integrationId, defaultId, CancellationToken.None)))!;

        Assert.False(result.Succeeded);
        Assert.Contains("'biostar-secret' is not configured", result.Message);
        Assert.Equal(0, _adapter.Calls);
    }

    [Fact]
    public async Task ARestApiTestCallsTheConnectorsAdapter_WithTheSecretAndSettings_AndSaysOnlyWhetherItWorked()
    {
        _secrets.Set("biostar-secret", "the-secret-value");
        var defaultId = await DefaultConnectionIdAsync(_integrationId);
        var input = (await Run(s => s.GetInputAsync(_integrationId, defaultId, CancellationToken.None)))!;
        input.Settings["baseUrl"] = "https://biostar.example.test:8443";
        await Run(s => s.UpdateAsync(_integrationId, defaultId, input, CancellationToken.None));

        var passed = (await Run(s => s.TestAsync(_integrationId, defaultId, CancellationToken.None)))!;
        _adapter.Result = new ConnectionValidationResult(false, "401 Unauthorized from https://biostar.example.test with password the-secret-value");
        var failed = (await Run(s => s.TestAsync(_integrationId, defaultId, CancellationToken.None)))!;

        Assert.True(passed.Succeeded);
        Assert.Equal(("the-secret-value", "https://biostar.example.test:8443"), (_adapter.LastConfig!.ConnectionSecret, _adapter.LastConfig.Settings["baseUrl"]));
        Assert.False(failed.Succeeded);

        // What the other end said (and a secret it echoed) is neither returned, stored nor audited.
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.DoesNotContain("Unauthorized", failed.Message);
        Assert.DoesNotContain("the-secret-value", db.AttendanceConnections.Single(c => c.Id == defaultId).LastTestMessage);
        Assert.All(db.AttendanceAuditLogs.Select(a => a.Details ?? string.Empty), text => Assert.DoesNotContain("the-secret-value", text));
        Assert.All(db.AttendanceAuditLogs.Select(a => a.Details ?? string.Empty), text => Assert.DoesNotContain("Unauthorized", text));
    }

    [Fact]
    public async Task AnAdapterThatThrows_OrNeverAnswers_GivesAFixedMessage()
    {
        _secrets.Set("biostar-secret", "the-secret-value");
        var defaultId = await DefaultConnectionIdAsync(_integrationId);

        _adapter.Throw = new HttpRequestException("connect to 10.0.0.9 refused; Authorization: Bearer the-secret-value");
        var unreachable = (await Run(s => s.TestAsync(_integrationId, defaultId, CancellationToken.None)))!;
        _adapter.Throw = null;
        _adapter.Hang = true;
        var silent = (await Run(s => s.TestAsync(_integrationId, defaultId, CancellationToken.None)))!;

        Assert.False(unreachable.Succeeded);
        Assert.Equal("The other end could not be reached.", unreachable.Message);
        Assert.False(silent.Succeeded);
        Assert.Contains("did not answer", silent.Message);
    }

    [Fact]
    public async Task AWebhookTestIsReadyOnlyWhenItsSecretIsConfigured_AndATransportWithoutAnAdapterSaysSo()
    {
        var push = (await Run(s => s.CreateAsync(_integrationId, Webhook("Push"), CancellationToken.None))).Id!.Value;
        var sftp = await AddSftpConnectionAsync();

        Assert.False((await Run(s => s.TestAsync(_integrationId, push, CancellationToken.None)))!.Succeeded);
        _secrets.Set("push-secret", "x");
        Assert.True((await Run(s => s.TestAsync(_integrationId, push, CancellationToken.None)))!.Succeeded);
        var notYet = (await Run(s => s.TestAsync(_integrationId, sftp, CancellationToken.None)))!;
        Assert.False(notYet.Succeeded);
        Assert.Contains("No adapter reads Sftp", notYet.Message);
        Assert.Null(await Run(s => s.TestAsync(_integrationId, 9999, CancellationToken.None)));
    }

    [Fact]
    public async Task TheIntegrationFormEditsTheDefaultConnection_AndAnIntegrationWithoutOneGetsItOnItsNextSave()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.RemoveRange(db.AttendanceConnections.Where(c => c.IntegrationId == _otherIntegrationId));
            db.SaveChanges();
        }

        using var context = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceIntegrationService(context, null!, new AttendanceAuditTrail(context, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog());
        var input = (await service.GetInputAsync(_otherIntegrationId, CancellationToken.None))!;
        Assert.Equal(("FileUpload", "None"), (input.Transport, input.AuthMethod));

        var saved = await service.UpdateAsync(_otherIntegrationId, input, CancellationToken.None);

        Assert.True(saved.Succeeded, string.Join(" ", saved.Errors.Values));
        var connection = context.AttendanceConnections.Single(c => c.IntegrationId == _otherIntegrationId);
        Assert.Equal(("Default", true, AttendanceTransport.FileUpload), (connection.Name, connection.IsDefault, connection.Transport));
    }

    private static ConnectionInput Webhook(string name) => new() { Name = name, Transport = "Webhook", AuthMethod = "HmacSignature", SecretRef = "push-secret" };

    private async Task<T> Run<T>(Func<AttendanceConnectionService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(Create(db));
    }

    private AttendanceConnectionService Create(RobeemDbContext db) => new(
        db,
        new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System),
        _secrets,
        new ServiceCollection().AddKeyedSingleton<IAttendanceAdapter>("suprema-biostar", _adapter).BuildServiceProvider(),
        TimeProvider.System,
        TimeSpan.FromMilliseconds(300));

    private async Task<int> DefaultConnectionIdAsync(int integrationId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Task.FromResult(db.AttendanceConnections.Single(c => c.IntegrationId == integrationId && c.IsDefault).Id);
    }

    private async Task<int> UploadConnectionIdAsync() => await DefaultConnectionIdAsync(_otherIntegrationId);

    private async Task<int> AddSftpConnectionAsync()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var connection = new AttendanceConnection { IntegrationId = _integrationId, Name = "Drop", Transport = AttendanceTransport.Sftp, AuthMethod = ConnectionAuthMethod.SshKey, SecretRef = "drop-key", Settings = "{\"host\":\"h\",\"remotePath\":\"/\",\"hostKeyFingerprint\":\"SHA256:x\"}" };
        db.AttendanceConnections.Add(connection);
        await db.SaveChangesAsync();
        _secrets.Set("drop-key", "k");
        return connection.Id;
    }

    private sealed class FakeAdapter : IAttendanceAdapter
    {
        public ConnectionValidationResult Result { get; set; } = new(true, "Logged in.");

        public Exception? Throw { get; set; }

        public bool Hang { get; set; }

        public int Calls { get; private set; }

        public AttendanceIntegrationConfig? LastConfig { get; private set; }

        public async Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken)
        {
            Calls++;
            LastConfig = config;
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Result;
        }

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
