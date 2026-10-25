using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The catalog an integration administrator manages: vendors, device types and the names of the transports.</summary>
public sealed class AttendanceCatalogServicesTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly int _vendorId;

    public AttendanceCatalogServicesTests()
    {
        _database.SeedAttendanceCatalog();
        using var db = _database.CreateContext(TestDatabase.TenantA);
        _vendorId = db.AttendanceVendors.Single(v => v.VendorKey == "generic-csv").Id;
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AVendor_IsCreatedWithItsTransportsAndDefaultFormat_AndListedAsUsable()
    {
        var formatId = FormatId("ZKTeco attendance export");

        var created = await Connectors(db => db.CreateAsync(NewConnector(formatId: formatId, transports: ["RestApi", "Webhook", "FileUpload", "RestApi"]), CancellationToken.None));

        Assert.True(created.Succeeded);
        var item = (await Connectors(db => db.ListAsync(CancellationToken.None))).Single(p => p.ConnectorKey == "acme-clock");
        Assert.Equal(("Acme clock", "ZKTeco attendance export", true, false, true, 0), (item.DisplayName, item.DefaultPayloadFormatName, item.IsActive, item.HasAdapter, item.IsUsable, item.IntegrationCount));
        Assert.Equal(["RestApi", "Webhook"], item.Transports);

        var input = (await Connectors(db => db.GetInputAsync(created.Id!.Value, CancellationToken.None)))!;
        Assert.Equal(("acme-clock", formatId), (input.ConnectorKey, input.DefaultPayloadFormatId));
        Assert.Equal(["RestApi", "Webhook"], input.Transports.Order());
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(check.AttendanceAuditLogs.ToList(), a => a.TargetType == nameof(AttendanceConnectorDefinition) && a.TargetId == created.Id);
    }

    [Fact]
    public async Task AVendor_IsUsableWhenItHasAFormatOrACodeAdapter_AndOnlyWhileActive()
    {
        var nothing = (await Connectors(db => db.CreateAsync(NewConnector(key: "nothing"), CancellationToken.None))).Id!.Value;
        var adapted = (await Connectors(db => db.CreateAsync(NewConnector(key: "adapted"), CancellationToken.None))).Id!.Value;
        var off = (await Connectors(db => db.CreateAsync(NewConnector(key: "off", formatId: FormatId("Generic CSV"), isActive: false), CancellationToken.None))).Id!.Value;

        var items = (await Connectors(db => db.ListAsync(CancellationToken.None), new StubAdapterCatalog("adapted"))).ToDictionary(p => p.Id);

        Assert.Equal((true, false, true), (items[adapted].IsUsable, items[nothing].IsUsable, items[adapted].HasAdapter));
        Assert.Equal((false, false), (items[off].IsUsable, items[off].IsActive));
        Assert.Equal(["generic-csv", "zkteco"], items.Values.Where(p => p.IsUsable && p.DefaultPayloadFormatId != null).Select(p => p.ConnectorKey).Order());
    }

    [Theory]
    [InlineData("", "ConnectorKey")]
    [InlineData("Has Capitals", "ConnectorKey")]
    [InlineData("-leading", "ConnectorKey")]
    [InlineData("zkteco", "ConnectorKey")]
    public async Task ABadOrRepeatedKey_IsRefused(string key, string field)
    {
        var result = await Connectors(db => db.CreateAsync(NewConnector(key: key), CancellationToken.None));

        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public async Task ABadVendor_IsRefused_FieldByField()
    {
        var input = NewConnector(transports: ["Carrier pigeon"], formatId: 999);
        input.DisplayName = " ";
        input.AdapterKey = "Not A Key";

        var result = await Connectors(db => db.CreateAsync(input, CancellationToken.None));

        Assert.Equal(["AdapterKey", "DefaultPayloadFormatId", "DisplayName", "Transports"], result.Errors.Keys.Order());
    }

    [Fact]
    public async Task AVendorIsUpdatedInPlace_ItsKeyNeverChanges_AndItsTransportsFollowTheInput()
    {
        var id = (await Connectors(db => db.CreateAsync(NewConnector(transports: ["RestApi"]), CancellationToken.None))).Id!.Value;

        var renamedKey = NewConnector(key: "other-key");
        var keyRefused = await Connectors(db => db.UpdateAsync(id, renamedKey, CancellationToken.None));

        var changed = NewConnector(transports: ["Webhook"], isActive: false);
        changed.DisplayName = "Acme, renamed";
        var updated = await Connectors(db => db.UpdateAsync(id, changed, CancellationToken.None));

        Assert.Contains("ConnectorKey", keyRefused.Errors.Keys);
        Assert.True(updated.Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var vendor = check.AttendanceConnectorDefinitions.Include(p => p.Transports).ThenInclude(t => t.Transport).Single(p => p.Id == id);
        Assert.Equal(("Acme, renamed", false), (vendor.DisplayName, vendor.IsActive));
        Assert.Equal(["Webhook"], vendor.Transports.Select(t => t.Transport!.TransportKey));
        Assert.True((await Connectors(db => db.UpdateAsync(999, changed, CancellationToken.None))).NotFound);
        Assert.Null(await Connectors(db => db.GetInputAsync(999, CancellationToken.None)));
    }

    [Fact]
    public async Task ARetiredFormat_CanNotBeChosenAnew_ButAVendorKeepsItsOwn()
    {
        var formatId = FormatId("Generic CSV");
        var id = (await Connectors(db => db.CreateAsync(NewConnector(formatId: formatId), CancellationToken.None))).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendancePayloadFormats.Single(f => f.Id == formatId).IsActive = false;
            db.SaveChanges();
        }

        var keeps = await Connectors(db => db.UpdateAsync(id, NewConnector(formatId: formatId), CancellationToken.None));
        var anew = await Connectors(db => db.CreateAsync(NewConnector(key: "second", formatId: formatId), CancellationToken.None));

        Assert.True(keeps.Succeeded);
        Assert.Contains("DefaultPayloadFormatId", anew.Errors.Keys);
    }

    [Fact]
    public async Task DeviceTypes_AreAddedRenamedAndRetired_NeverDeleted_AndOnlyActiveOnesAreOptions()
    {
        var added = await DeviceTypes(db => db.CreateAsync(new DeviceTypeInput { Name = "  Palm reader " }, CancellationToken.None));
        var duplicate = await DeviceTypes(db => db.CreateAsync(new DeviceTypeInput { Name = "palm READER" }, CancellationToken.None));
        var blank = await DeviceTypes(db => db.CreateAsync(new DeviceTypeInput { Name = " " }, CancellationToken.None));
        var retired = await DeviceTypes(db => db.UpdateAsync(added.Id!.Value, new DeviceTypeInput { Name = "Palm reader", IsActive = false }, CancellationToken.None));
        var clash = await DeviceTypes(db => db.UpdateAsync(added.Id!.Value, new DeviceTypeInput { Name = "Facial" }, CancellationToken.None));

        Assert.True(added.Succeeded);
        Assert.Contains("Name", duplicate.Errors.Keys);
        Assert.Contains("Name", blank.Errors.Keys);
        Assert.True(retired.Succeeded);
        Assert.Contains("Name", clash.Errors.Keys);
        Assert.DoesNotContain("Palm reader", (await DeviceTypes(db => db.ListOptionsAsync(CancellationToken.None))).Select(o => o.Name));
        Assert.Equal(7, (await DeviceTypes(db => db.ListAsync(CancellationToken.None))).Count);
        Assert.True((await DeviceTypes(db => db.UpdateAsync(999, new DeviceTypeInput { Name = "x" }, CancellationToken.None))).NotFound);
        var input = (await DeviceTypes(db => db.GetInputAsync(added.Id!.Value, CancellationToken.None)))!;
        Assert.Equal(("Palm reader", false), (input.Name, input.IsActive));
    }

    [Fact]
    public async Task ADeviceTypesCount_IsTheDevicesThatHaveIt()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var integration = new AttendanceIntegration { Connector = AttendanceTestData.Seeded(db, "zkteco"), DisplayName = "HQ" };
            var facial = db.AttendanceDeviceTypes.Single(t => t.Name == "Facial");
            db.AttendanceDevices.AddRange(
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "A", TimeZoneId = "UTC", DeviceType = facial },
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "2", Name = "B", TimeZoneId = "UTC", DeviceType = facial });
            db.SaveChanges();
        }

        var counts = (await DeviceTypes(db => db.ListAsync(CancellationToken.None))).ToDictionary(t => t.Name, t => t.DeviceCount);

        Assert.Equal((2, 0), (counts["Facial"], counts["Biometric"]));
    }

    [Fact]
    public async Task ATransport_CanBeRenamedOrSwitchedOff_ButNotAdded_AndFileUploadStaysOn()
    {
        var list = await Transports(db => db.ListAsync(CancellationToken.None));
        Assert.Equal(Enum.GetNames<AttendanceTransport>(), list.Select(t => t.Key));
        var sftp = list.Single(t => t.Key == "Sftp");
        var upload = list.Single(t => t.Key == "FileUpload");
        Assert.Equal((true, false), (sftp.IsPull, sftp.UsesPayloadFormat));

        var renamed = await Transports(db => db.UpdateAsync(sftp.Id, new TransportInput { DisplayName = " Secure FTP ", IsActive = false }, CancellationToken.None));
        var blank = await Transports(db => db.UpdateAsync(sftp.Id, new TransportInput { DisplayName = " " }, CancellationToken.None));
        var uploadOff = await Transports(db => db.UpdateAsync(upload.Id, new TransportInput { DisplayName = "Upload", IsActive = false }, CancellationToken.None));

        Assert.True(renamed.Succeeded);
        Assert.Contains("DisplayName", blank.Errors.Keys);
        Assert.Contains("IsActive", uploadOff.Errors.Keys);
        var input = (await Transports(db => db.GetInputAsync(sftp.Id, CancellationToken.None)))!;
        Assert.Equal(("Secure FTP", false), (input.DisplayName, input.IsActive));

        // The flags that say what a transport does are not editable: only its name and availability changed.
        var after = (await Transports(db => db.ListAsync(CancellationToken.None))).Single(t => t.Key == "Sftp");
        Assert.Equal((true, false), (after.IsPull, after.UsesPayloadFormat));
        Assert.True((await Transports(db => db.UpdateAsync(999, new TransportInput { DisplayName = "x" }, CancellationToken.None))).NotFound);
    }

    [Fact]
    public async Task ASwitchedOffTransport_CanNotBeChosenForANewIntegration_ButAnExistingOneKeepsIt()
    {
        int integrationId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var biostar = AttendanceTestData.Seeded(db, "suprema-biostar");
            var integration = new AttendanceIntegration { Connector = biostar, DisplayName = "BioStar" }.WithConnection(AttendanceTransport.RestApi);
            db.Add(integration);
            db.SaveChanges();
            integrationId = integration.Id;
            db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "RestApi").IsActive = false;
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var service = new AttendanceIntegrationService(check, new NoSync(), new AttendanceAuditTrail(check, new TestUser(7), TimeProvider.System), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog("suprema-biostar"));
        var input = (await service.GetInputAsync(integrationId, CancellationToken.None))!;

        var keeps = await service.UpdateAsync(integrationId, input, CancellationToken.None);
        var fresh = await service.CreateAsync(new IntegrationInput { ConnectorId = input.ConnectorId, DisplayName = "Another", Transport = "RestApi", Status = "Active" }, CancellationToken.None);

        Assert.True(keeps.Succeeded);
        Assert.Contains("not available for new integrations", fresh.Errors[nameof(IntegrationInput.Transport)]);
    }

    [Fact]
    public async Task ASwitchedOffTransport_CanNotBeAddedToAVendor_ButAVendorKeepsItsOwn()
    {
        var id = (await Connectors(db => db.CreateAsync(NewConnector(transports: ["Sftp"]), CancellationToken.None))).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").IsActive = false;
            db.SaveChanges();
        }

        var keeps = await Connectors(db => db.UpdateAsync(id, NewConnector(transports: ["Sftp"]), CancellationToken.None));
        var anew = await Connectors(db => db.CreateAsync(NewConnector(key: "second", transports: ["Sftp"]), CancellationToken.None));
        var addsActive = await Connectors(db => db.UpdateAsync(id, NewConnector(transports: ["Sftp", "RestApi"]), CancellationToken.None));
        var dropsIt = await Connectors(db => db.UpdateAsync(id, NewConnector(transports: ["RestApi"]), CancellationToken.None));
        var takesItBack = await Connectors(db => db.UpdateAsync(id, NewConnector(transports: ["Sftp", "RestApi"]), CancellationToken.None));

        Assert.True(keeps.Succeeded);
        Assert.Contains("switched off", anew.Errors[nameof(ConnectorInput.Transports)]);
        Assert.True(addsActive.Succeeded);
        Assert.True(dropsIt.Succeeded);

        // Once removed, a switched-off transport can't be added back until it is switched on again.
        Assert.Contains("switched off", takesItBack.Errors[nameof(ConnectorInput.Transports)]);
    }

    private ConnectorInput NewConnector(string key = "acme-clock", int? formatId = null, string[]? transports = null, bool isActive = true) => new()
    {
        VendorId = _vendorId,
        ConnectorKey = key,
        DisplayName = "Acme clock",
        Description = "Acme's clock software",
        AdapterKey = key,
        DefaultPayloadFormatId = formatId,
        Transports = [.. transports ?? []],
        IsActive = isActive,
    };

    private AttendanceAuditTrail Audit(RobeemDbContext db) => new(db, new TestUser(7), TimeProvider.System);

    private int FormatId(string name)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendancePayloadFormats.Single(f => f.Name == name).Id;
    }

    private async Task<T> Connectors<T>(Func<AttendanceConnectorService, Task<T>> action, IAttendanceAdapterCatalog? adapters = null)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceConnectorService(db, Audit(db), adapters ?? new StubAdapterCatalog()));
    }

    private async Task<T> DeviceTypes<T>(Func<AttendanceDeviceTypeService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceDeviceTypeService(db, Audit(db)));
    }

    private async Task<T> Transports<T>(Func<AttendanceTransportService, Task<T>> action)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceTransportService(db, Audit(db)));
    }

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
