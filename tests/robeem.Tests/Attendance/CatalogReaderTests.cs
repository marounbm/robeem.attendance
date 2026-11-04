using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Security;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>
/// How tenant code reads the prime tenant's catalog (by key, never by foreign key), and who may change it.
/// </summary>
public sealed class CatalogReaderTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public CatalogReaderTests()
    {
        _database.SeedAttendanceCatalog();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AConnector_IsReadByKey_WithItsVendorItsTransportsAndItsDefaultFormat()
    {
        var reader = _database.CreateCatalogReader(new StubAdapterCatalog("suprema-biostar"));

        var zk = (await reader.GetConnectorAsync("zkteco", CancellationToken.None))!;
        var biostar = (await reader.GetConnectorAsync("suprema-biostar", CancellationToken.None))!;
        var webhook = (await reader.GetConnectorAsync("generic-webhook", CancellationToken.None))!;

        Assert.Equal(("zkteco", "ZKTeco attendance export", true, null), (zk.VendorKey, zk.DefaultPayloadFormatName, zk.IsUsable, zk.AdapterKey));
        Assert.Equal(["RestApi"], biostar.Transports);
        Assert.Equal((true, true), (biostar.IsUsable, biostar.SupportsIncrementalSync));
        Assert.False(webhook.IsUsable);
        Assert.Null(await reader.GetConnectorAsync("no-such-connector", CancellationToken.None));
    }

    [Fact]
    public async Task SeveralConnectors_AreReadTogether_AnUnknownKeyIsJustMissing()
    {
        var found = await _database.CreateCatalogReader().GetConnectorsAsync(["zkteco", "generic-csv", "nope", "zkteco"], CancellationToken.None);

        Assert.Equal(["generic-csv", "zkteco"], found.Keys.Order());
        Assert.Empty(await _database.CreateCatalogReader().GetConnectorsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task AFormatByName_IsItsLatestVersion_AnExactVersionStaysReadable_AndTheEventCodesCome()
    {
        int firstId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            firstId = _database.CreateCatalog().AttendancePayloadFormats.Single(f => f.Name == "ZKTeco attendance export").Id;
            var changed = (await _database.PayloadFormatService(db).GetInputAsync(firstId, CancellationToken.None))!;
            changed.TimestampFormats = "yyyy-MM-dd HH:mm";
            Assert.True((await _database.PayloadFormatService(db).UpdateAsync(firstId, changed, CancellationToken.None)).Succeeded);
        }

        var reader = _database.CreateCatalogReader();
        var latest = (await reader.GetLatestFormatAsync("ZKTeco attendance export", CancellationToken.None))!;
        var first = (await reader.GetFormatVersionAsync("ZKTeco attendance export", 1, CancellationToken.None))!;
        var many = await reader.GetLatestFormatsAsync(["ZKTeco attendance export", "Generic CSV", "Missing"], CancellationToken.None);
        var listed = await reader.ListLatestFormatsAsync(CancellationToken.None);

        Assert.Equal((2, firstId), (latest.Version, latest.SupersedesId));
        Assert.Equal((1, firstId), (first.Version, first.Id));
        Assert.NotEmpty(latest.EventCodes);
        Assert.Equal(["Generic CSV", "ZKTeco attendance export"], many.Keys.Order());
        Assert.Equal(2, many["ZKTeco attendance export"].Version);
        Assert.Equal(3, listed.Count);
        Assert.Null(await reader.GetFormatVersionAsync("ZKTeco attendance export", 9, CancellationToken.None));
        Assert.Null(await reader.GetLatestFormatAsync("Missing", CancellationToken.None));
    }

    [Fact]
    public async Task TheTransportsAndDeviceTypes_AreListedByName()
    {
        var reader = _database.CreateCatalogReader();

        Assert.Equal(6, (await reader.ListTransportsAsync(CancellationToken.None)).Count);
        Assert.Equal(["Biometric", "Cloud", "Facial", "Mobile", "OnPrem", "RFID"], (await reader.ListDeviceTypesAsync(CancellationToken.None)).Select(t => t.Name));
    }

    [Fact]
    public void AChangeToTheCatalog_IsAllowedForAPlatformAdministratorOfThePrimeTenant_OnlyThere()
    {
        var platform = new TestUser(1, CatalogWriteAccess.PlatformRole);
        var integration = new TestUser(1, "IntegrationAdministrator");

        AttendanceTestData.WriteAccess(TestDatabase.Prime, platform).EnsureCanWrite();
        AttendanceTestData.WriteAccess(TestDatabase.Prime, new SystemCurrentUser()).EnsureCanWrite();
        Assert.Throws<CatalogReadOnlyException>(() => AttendanceTestData.WriteAccess(TestDatabase.TenantA, platform).EnsureCanWrite());
        Assert.Throws<CatalogReadOnlyException>(() => AttendanceTestData.WriteAccess(TestDatabase.Prime, integration).EnsureCanWrite());
    }

    [Fact]
    public async Task EveryCatalogServiceRefusesAChange_FromATenantThatIsNotThePrimeOne_AndChangesNothing()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var platform = new TestUser(1, CatalogWriteAccess.PlatformRole);

        await Assert.ThrowsAsync<CatalogReadOnlyException>(() => _database.VendorService(db, TestDatabase.TenantA, platform).CreateAsync(new VendorInput { VendorKey = "acme", DisplayName = "Acme" }, CancellationToken.None));
        await Assert.ThrowsAsync<CatalogReadOnlyException>(() => _database.DeviceTypeService(db, TestDatabase.TenantA, platform).CreateAsync(new DeviceTypeInput { Name = "Palm reader" }, CancellationToken.None));
        await Assert.ThrowsAsync<CatalogReadOnlyException>(() => _database.TransportService(db, TestDatabase.TenantA, platform).UpdateAsync(1, new TransportInput { DisplayName = "x" }, CancellationToken.None));
        await Assert.ThrowsAsync<CatalogReadOnlyException>(() => _database.ConnectorService(db, tenantId: TestDatabase.TenantA, user: platform).CreateAsync(new ConnectorInput { ConnectorKey = "acme", DisplayName = "Acme", VendorId = 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<CatalogReadOnlyException>(() => _database.PayloadFormatService(db, TestDatabase.TenantA, platform).CreateAsync(new PayloadFormatInput { Name = "Mine" }, CancellationToken.None));

        // Reading is open to the tenant.
        Assert.NotEmpty(await _database.VendorService(db, TestDatabase.TenantA, platform).ListAsync(CancellationToken.None));
        Assert.NotEmpty(await _database.PayloadFormatService(db, TestDatabase.TenantA, platform).ListAsync(CancellationToken.None));
        using var check = _database.CreateCatalog();
        Assert.Equal(4, check.AttendanceVendors.Count());
        Assert.Empty(db.AttendanceAuditLogs);
    }

    [Fact]
    public async Task ThePrimeTenant_ChangesTheCatalog_AndEveryOtherTenantSeesItAtOnce()
    {
        using (var prime = _database.CreateContext(TestDatabase.Prime))
        {
            var created = await _database.PayloadFormatService(prime, TestDatabase.Prime, new TestUser(1, CatalogWriteAccess.PlatformRole))
                .CreateAsync(new PayloadFormatInput { Name = "New offering", EmployeeColumn = "Badge", TimestampColumn = "Time", TimestampFormats = "yyyy-MM-dd HH:mm" }, CancellationToken.None);
            Assert.True(created.Succeeded, string.Join(" ", created.Errors.Values));
        }

        using var tenant = _database.CreateContext(TestDatabase.TenantA);
        var listed = await _database.PayloadFormatService(tenant, TestDatabase.TenantA).ListAsync(CancellationToken.None);
        Assert.Contains(listed, f => f.Name == "New offering");
        Assert.NotNull(await _database.CreateCatalogReader().GetLatestFormatAsync("New offering", CancellationToken.None));

        // The audit entry of the change is the prime tenant's own.
        using var primeCheck = _database.CreateContext(TestDatabase.Prime);
        Assert.Contains(primeCheck.AttendanceAuditLogs, a => a.TargetType == nameof(AttendancePayloadFormat));
        Assert.Empty(tenant.AttendanceAuditLogs);
    }
}
