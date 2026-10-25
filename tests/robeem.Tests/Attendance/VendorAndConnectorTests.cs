using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The vendor (the company) and its connectors (each software or protocol it offers).</summary>
public sealed class VendorAndConnectorTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly string _csvPath = Path.GetTempFileName();

    public VendorAndConnectorTests()
    {
        _database.SeedAttendanceCatalog();
    }

    public void Dispose()
    {
        File.Delete(_csvPath);
        _database.Dispose();
    }

    [Fact]
    public async Task AVendor_IsCreatedAndUpdated_ItsKeyIsUniqueAndNeverChanges_AndItIsListedWithItsCounts()
    {
        var created = await Vendors(v => v.CreateAsync(NewVendor("acme"), CancellationToken.None));
        var duplicate = await Vendors(v => v.CreateAsync(NewVendor("acme"), CancellationToken.None));
        var renamedKey = await Vendors(v => v.UpdateAsync(created.Id!.Value, NewVendor("other"), CancellationToken.None));
        var changed = NewVendor("acme");
        changed.DisplayName = "Acme, renamed";
        changed.IsActive = false;
        var updated = await Vendors(v => v.UpdateAsync(created.Id!.Value, changed, CancellationToken.None));

        Assert.True(created.Succeeded);
        Assert.Contains(nameof(VendorInput.VendorKey), duplicate.Errors.Keys);
        Assert.Contains(nameof(VendorInput.VendorKey), renamedKey.Errors.Keys);
        Assert.True(updated.Succeeded);

        var list = await Vendors(v => v.ListAsync(CancellationToken.None));
        var acme = list.Single(v => v.VendorKey == "acme");
        Assert.Equal(("Acme, renamed", false, 0, 0), (acme.DisplayName, acme.IsActive, acme.ConnectorCount, acme.IntegrationCount));
        Assert.Equal(1, list.Single(v => v.VendorKey == "zkteco").ConnectorCount);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(2, check.AttendanceAuditLogs.Count(a => a.TargetType == nameof(AttendanceVendor)));
    }

    [Fact]
    public async Task AVendorCanHaveSeveralConnectors_EachWithItsOwnTransportsAndFormat()
    {
        var vendorId = VendorId("zkteco");

        var api = await Connectors(c => c.CreateAsync(NewConnector(vendorId, "zkteco-api", "RestApi"), CancellationToken.None));
        var push = await Connectors(c => c.CreateAsync(NewConnector(vendorId, "zkteco-push", "Webhook"), CancellationToken.None));

        Assert.True(api.Succeeded && push.Succeeded);
        var list = await Connectors(c => c.ListAsync(CancellationToken.None));
        var zkteco = list.Where(c => c.VendorId == vendorId).ToDictionary(c => c.ConnectorKey);
        Assert.Equal(["zkteco", "zkteco-api", "zkteco-push"], zkteco.Keys.Order());
        Assert.Equal(["RestApi"], zkteco["zkteco-api"].Transports);
        Assert.Equal(["Webhook"], zkteco["zkteco-push"].Transports);
        Assert.Empty(zkteco["zkteco"].Transports);
        Assert.Equal("ZKTeco attendance export", zkteco["zkteco"].DefaultPayloadFormatName);
        Assert.All(zkteco.Values, c => Assert.Equal("ZKTeco", c.VendorName));
        Assert.Equal(3, (await Vendors(v => v.ListAsync(CancellationToken.None))).Single(v => v.VendorKey == "zkteco").ConnectorCount);
    }

    [Fact]
    public async Task AConnectorsKeyAndVendor_NeverChange_AndItsKeyIsUnique()
    {
        var vendorId = VendorId("zkteco");
        var id = (await Connectors(c => c.CreateAsync(NewConnector(vendorId, "zkteco-api", "RestApi"), CancellationToken.None))).Id!.Value;

        var duplicate = await Connectors(c => c.CreateAsync(NewConnector(vendorId, "zkteco-api"), CancellationToken.None));
        var renamed = await Connectors(c => c.UpdateAsync(id, NewConnector(vendorId, "renamed", "RestApi"), CancellationToken.None));
        var moved = await Connectors(c => c.UpdateAsync(id, NewConnector(VendorId("generic-csv"), "zkteco-api", "RestApi"), CancellationToken.None));
        var unknownVendor = await Connectors(c => c.CreateAsync(NewConnector(9999, "orphan"), CancellationToken.None));

        Assert.Contains(nameof(ConnectorInput.ConnectorKey), duplicate.Errors.Keys);
        Assert.Contains(nameof(ConnectorInput.ConnectorKey), renamed.Errors.Keys);
        Assert.Contains(nameof(ConnectorInput.VendorId), moved.Errors.Keys);
        Assert.Contains(nameof(ConnectorInput.VendorId), unknownVendor.Errors.Keys);
    }

    [Fact]
    public async Task AConnector_IsNotUsableWhileItsVendorIsInactive()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceVendors.Single(v => v.VendorKey == "zkteco").IsActive = false;
            db.SaveChanges();
        }

        var zkteco = (await Connectors(c => c.ListAsync(CancellationToken.None))).Single(c => c.ConnectorKey == "zkteco");
        var csv = (await Connectors(c => c.ListAsync(CancellationToken.None))).Single(c => c.ConnectorKey == "generic-csv");

        Assert.Equal((true, false), (zkteco.IsActive, zkteco.IsUsable));
        Assert.True(csv.IsUsable);
    }

    [Fact]
    public async Task AnEncryptedPayload_CanNotBeChosenWhereNoDecryptorIsRegistered_AndAnUnknownKindNever()
    {
        var input = NewConnector(VendorId("zkteco"), "encrypted");
        input.EncryptionKind = "Pgp";

        var result = await Connectors(c => c.CreateAsync(input, CancellationToken.None));
        var unknown = NewConnector(VendorId("zkteco"), "odd");
        unknown.EncryptionKind = "Rot13";

        Assert.Contains(nameof(ConnectorInput.EncryptionKind), result.Errors.Keys);
        Assert.Contains(nameof(ConnectorInput.EncryptionKind), (await Connectors(c => c.CreateAsync(unknown, CancellationToken.None))).Errors.Keys);
    }

    [Fact]
    public async Task TwoConnectorsOfOneVendor_BothRecordTheVendorsKeyAsTheSourceOfTheirPunches()
    {
        File.WriteAllText(_csvPath, "employee_id,timestamp,type,device_id\r\nE-1,2026-10-04 09:00,IN,DEV-1\r\n");
        var vendorId = VendorId("generic-csv");
        var second = (await Connectors(c => c.CreateAsync(NewConnector(vendorId, "generic-csv-b", defaultFormat: "Generic CSV"), CancellationToken.None))).Id!.Value;
        int first;
        int other;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var one = new AttendanceIntegration { Connector = AttendanceTestData.Seeded(db, "generic-csv"), DisplayName = "First" };
            var two = new AttendanceIntegration { ConnectorId = second, DisplayName = "Second" };
            db.AddRange(one, two);
            db.SaveChanges();
            (first, other) = (one.Id, two.Id);
        }

        using var services = new ServiceCollection()
            .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
            .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
            .BuildServiceProvider();
        foreach (var id in new[] { first, other })
        {
            using var db = _database.CreateContext(TestDatabase.TenantA);
            var result = await new IngestionService(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), services)
                .IngestManualUploadAsync(id, _csvPath, CancellationToken.None);
            Assert.Equal(1, result!.RecordsIngested);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["generic-csv", "generic-csv"], check.RawAttendanceTransactions.Select(r => r.SourceSystem).ToArray());
        Assert.Equal(2, check.RawAttendanceTransactions.Select(r => r.IntegrationId).Distinct().Count());
    }

    [Fact]
    public async Task AnIntegrationIsMadeWithAConnector_AndTheListSaysWhichAndWhoseItIs()
    {
        var vendorId = VendorId("zkteco");
        var connectorId = (await Connectors(c => c.CreateAsync(NewConnector(vendorId, "zkteco-export-2", defaultFormat: "ZKTeco attendance export"), CancellationToken.None))).Id!.Value;
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceIntegrationService(db, new NoSync(), Audit(db), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog());

        var created = await service.CreateAsync(new IntegrationInput { ConnectorId = connectorId, DisplayName = "Branch", Transport = "FileUpload", Status = "Active" }, CancellationToken.None);

        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));
        var item = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.Equal(("Acme software", "ZKTeco", "zkteco"), (item.ConnectorName, item.VendorName, item.VendorKey));
        var options = await service.ListConnectorsAsync(CancellationToken.None);
        Assert.Contains(options, o => o.Id == connectorId && o.VendorKey == "zkteco" && o.IsActive);
    }

    private static VendorInput NewVendor(string key) => new() { VendorKey = key, DisplayName = "Acme", WebsiteUrl = "https://acme.example.test" };

    private ConnectorInput NewConnector(int vendorId, string key, string? transport = null, string? defaultFormat = null) => new()
    {
        VendorId = vendorId,
        ConnectorKey = key,
        DisplayName = "Acme software",
        AdapterKey = transport is null ? null : key,
        Transports = transport is null ? [] : [transport],
        DefaultPayloadFormatId = defaultFormat is null ? null : FormatId(defaultFormat),
    };

    private AttendanceAuditTrail Audit(RobeemDbContext db) => new(db, new TestUser(7), TimeProvider.System);

    private int VendorId(string key)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceVendors.Single(v => v.VendorKey == key).Id;
    }

    private int FormatId(string name)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendancePayloadFormats.Single(f => f.Name == name).Id;
    }

    private async Task<T> Vendors<T>(Func<AttendanceVendorService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceVendorService(db, Audit(db)));
    }

    private async Task<T> Connectors<T>(Func<AttendanceConnectorService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceConnectorService(db, Audit(db), new StubAdapterCatalog()));
    }

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
