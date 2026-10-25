using System.Text;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>A webhook is accepted through a connection, and only when it proves itself as that connection's authentication says.</summary>
public sealed class WebhookReceiverTests : IDisposable
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"events":[]}""");

    private readonly TestDatabase _database = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly CapturingIngestion _ingestion = new();
    private readonly int _integrationId;
    private readonly int _connectionId;

    public WebhookReceiverTests()
    {
        _database.SeedAttendanceCatalog();
        _secrets.Set("hook-secret", "shared-secret");
        using var db = _database.CreateContext(TestDatabase.TenantA);

        // The built-in webhook vendor is seeded switched off, until an administrator turns it on.
        var connector = AttendanceTestData.Seeded(db, "generic-webhook");
        connector.IsActive = true;
        db.AttendanceVendors.Single(v => v.Id == connector.VendorId).IsActive = true;
        var integration = new AttendanceIntegration { Connector = connector, DisplayName = "Pushed" }
            .WithConnection(AttendanceTransport.Webhook, "hook-secret");
        db.Add(integration);
        db.SaveChanges();
        _integrationId = integration.Id;
        _connectionId = db.AttendanceConnections.Single(c => c.IntegrationId == integration.Id).Id;
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ASignedBody_IsAccepted_ThroughTheDefaultWebhookConnection_WhenTheRouteNamesNone()
    {
        var receipt = await ReceiveAsync(null, new WebhookCredentials(Signature: WebhookSignature.Compute("shared-secret", Body)));

        Assert.Equal(WebhookOutcome.Accepted, receipt.Outcome);
        Assert.Equal((_integrationId, _connectionId), (_ingestion.IntegrationId, _ingestion.ConnectionId));
    }

    [Fact]
    public async Task ARouteThatNamesTheConnection_IsAcceptedThroughIt_AndOneThatNamesAnotherIsNot()
    {
        var signed = new WebhookCredentials(Signature: WebhookSignature.Compute("shared-secret", Body));

        Assert.Equal(WebhookOutcome.Accepted, (await ReceiveAsync(_connectionId, signed)).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(_connectionId + 100, signed)).Outcome);
    }

    [Fact]
    public async Task AWrongOrMissingSignature_IsUnauthorized_AndNothingIsIngested()
    {
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials(Signature: WebhookSignature.Compute("another-secret", Body)))).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials())).Outcome);
        Assert.Equal(0, _ingestion.Calls);
    }

    [Fact]
    public async Task AConnectionWithoutAConfiguredSecret_AcceptsNothing()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.Single(c => c.Id == _connectionId).SecretRef = "never-set";
            db.SaveChanges();
        }

        var receipt = await ReceiveAsync(null, new WebhookCredentials(Signature: WebhookSignature.Compute(string.Empty, Body)));

        Assert.Equal(WebhookOutcome.Unauthorized, receipt.Outcome);
    }

    [Fact]
    public async Task AnApiKeyConnection_AcceptsTheKeyInItsHeader_AndABearerOneTheTokenInAuthorization()
    {
        await SetMethodAsync(ConnectionAuthMethod.ApiKey);
        Assert.Equal(WebhookOutcome.Accepted, (await ReceiveAsync(null, new WebhookCredentials(ApiKey: "shared-secret"))).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials(ApiKey: "shared-secreT"))).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials(Signature: WebhookSignature.Compute("shared-secret", Body)))).Outcome);

        await SetMethodAsync(ConnectionAuthMethod.BearerToken);
        Assert.Equal(WebhookOutcome.Accepted, (await ReceiveAsync(null, new WebhookCredentials(Authorization: "Bearer shared-secret"))).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials(Authorization: "shared-secret"))).Outcome);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, new WebhookCredentials(Authorization: "Bearer "))).Outcome);
    }

    [Fact]
    public async Task ASwitchedOffConnection_APausedIntegration_AndAConnectionThatIsNotAWebhook_AreNotAccepted()
    {
        var signed = new WebhookCredentials(Signature: WebhookSignature.Compute("shared-secret", Body));

        await Mutate(db => db.AttendanceConnections.Single(c => c.Id == _connectionId).IsActive = false);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, signed)).Outcome);

        await Mutate(db => db.AttendanceConnections.Single(c => c.Id == _connectionId).IsActive = true);
        await Mutate(db => db.AttendanceIntegrations.Single(i => i.Id == _integrationId).Status = AttendanceIntegrationStatus.Paused);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, signed)).Outcome);

        await Mutate(db => db.AttendanceIntegrations.Single(i => i.Id == _integrationId).Status = AttendanceIntegrationStatus.Active);
        await Mutate(db => db.AttendanceConnections.Single(c => c.Id == _connectionId).Transport = AttendanceTransport.FileUpload);
        Assert.Equal(WebhookOutcome.Unauthorized, (await ReceiveAsync(null, signed)).Outcome);
    }

    [Fact]
    public async Task WhenTheDefaultConnectionIsNotAWebhook_TheOldRouteFallsBackToTheIntegrationsWebhookConnection()
    {
        int upload;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceConnections.Single(c => c.Id == _connectionId).IsDefault = false;
            db.SaveChanges();
            var connection = new AttendanceConnection { IntegrationId = _integrationId, Name = "Upload", IsDefault = true, Transport = AttendanceTransport.FileUpload };
            db.AttendanceConnections.Add(connection);
            db.SaveChanges();
            upload = connection.Id;
        }

        var receipt = await ReceiveAsync(null, new WebhookCredentials(Signature: WebhookSignature.Compute("shared-secret", Body)));

        Assert.Equal(WebhookOutcome.Accepted, receipt.Outcome);
        Assert.Equal(_connectionId, _ingestion.ConnectionId);
        Assert.NotEqual(upload, _ingestion.ConnectionId);
    }

    private async Task Mutate(Action<RobeemDbContext> change)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        change(db);
        await db.SaveChangesAsync();
    }

    private Task SetMethodAsync(ConnectionAuthMethod method) =>
        Mutate(db => db.AttendanceConnections.Single(c => c.Id == _connectionId).AuthMethod = method);

    private async Task<WebhookReceipt> ReceiveAsync(int? connectionId, WebhookCredentials credentials)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new WebhookReceiver(db, _secrets, _ingestion).ReceiveAsync(_integrationId, connectionId, Body, credentials, CancellationToken.None);
    }

    private sealed class CapturingIngestion : IIngestionService
    {
        public int Calls { get; private set; }

        public int? IntegrationId { get; private set; }

        public int? ConnectionId { get; private set; }

        public Task<IReadOnlyList<UploadTarget>> ListUploadTargetsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceIngestionResult?> IngestManualUploadAsync(int integrationId, string filePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceIngestionResult?> IngestUploadedFileAsync(int integrationId, string filePath, string fileName, int? uploadedById, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<UploadedFileItem>> ListUploadedFilesAsync(int? integrationId, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceIngestionResult?> IngestWebhookPayloadAsync(int integrationId, int? connectionId, string filePath, CancellationToken cancellationToken)
        {
            Calls++;
            (IntegrationId, ConnectionId) = (integrationId, connectionId);
            return Task.FromResult<AttendanceIngestionResult?>(new AttendanceIngestionResult { BatchId = 1, Status = nameof(AttendanceBatchStatus.Succeeded) });
        }
    }
}
