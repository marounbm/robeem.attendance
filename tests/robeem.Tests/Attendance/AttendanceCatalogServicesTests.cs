using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The catalog an integration administrator manages: providers, device types and the names of the transports.</summary>
public sealed class AttendanceCatalogServicesTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public AttendanceCatalogServicesTests()
    {
        _database.SeedAttendanceCatalog();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AProvider_IsCreatedWithItsTransportsAndDefaultFormat_AndListedAsUsable()
    {
        var formatId = FormatId("ZKTeco attendance export");

        var created = await Providers(db => db.CreateAsync(NewProvider(formatId: formatId, transports: ["RestApi", "Webhook", "FileUpload", "RestApi"]), CancellationToken.None));

        Assert.True(created.Succeeded);
        var item = (await Providers(db => db.ListAsync(CancellationToken.None))).Single(p => p.ProviderKey == "acme-clock");
        Assert.Equal(("Acme clock", "ZKTeco attendance export", true, false, true, 0), (item.DisplayName, item.DefaultFileFormatName, item.IsActive, item.HasAdapter, item.IsUsable, item.IntegrationCount));
        Assert.Equal(["RestApi", "Webhook"], item.Transports);

        var input = (await Providers(db => db.GetInputAsync(created.Id!.Value, CancellationToken.None)))!;
        Assert.Equal(("acme-clock", formatId), (input.ProviderKey, input.DefaultFileFormatId));
        Assert.Equal(["RestApi", "Webhook"], input.Transports.Order());
        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Contains(check.AttendanceAuditLogs.ToList(), a => a.TargetType == nameof(AttendanceProvider) && a.TargetId == created.Id);
    }

    [Fact]
    public async Task AProvider_IsUsableWhenItHasAFormatOrACodeAdapter_AndOnlyWhileActive()
    {
        var nothing = (await Providers(db => db.CreateAsync(NewProvider(key: "nothing"), CancellationToken.None))).Id!.Value;
        var adapted = (await Providers(db => db.CreateAsync(NewProvider(key: "adapted"), CancellationToken.None))).Id!.Value;
        var off = (await Providers(db => db.CreateAsync(NewProvider(key: "off", formatId: FormatId("Generic CSV"), isActive: false), CancellationToken.None))).Id!.Value;

        var items = (await Providers(db => db.ListAsync(CancellationToken.None), new StubAdapterCatalog("adapted"))).ToDictionary(p => p.Id);

        Assert.Equal((true, false, true), (items[adapted].IsUsable, items[nothing].IsUsable, items[adapted].HasAdapter));
        Assert.Equal((false, false), (items[off].IsUsable, items[off].IsActive));
        Assert.Equal(["generic-csv", "zkteco"], items.Values.Where(p => p.IsUsable && p.DefaultFileFormatId != null).Select(p => p.ProviderKey).Order());
    }

    [Theory]
    [InlineData("", "ProviderKey")]
    [InlineData("Has Capitals", "ProviderKey")]
    [InlineData("-leading", "ProviderKey")]
    [InlineData("zkteco", "ProviderKey")]
    public async Task ABadOrRepeatedKey_IsRefused(string key, string field)
    {
        var result = await Providers(db => db.CreateAsync(NewProvider(key: key), CancellationToken.None));

        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public async Task ABadProvider_IsRefused_FieldByField()
    {
        var input = NewProvider(transports: ["Carrier pigeon"], formatId: 999);
        input.DisplayName = " ";
        input.WebsiteUrl = "ftp://example.test";

        var result = await Providers(db => db.CreateAsync(input, CancellationToken.None));

        Assert.Equal(["DefaultFileFormatId", "DisplayName", "Transports", "WebsiteUrl"], result.Errors.Keys.Order());
    }

    [Fact]
    public async Task AProviderIsUpdatedInPlace_ItsKeyNeverChanges_AndItsTransportsFollowTheInput()
    {
        var id = (await Providers(db => db.CreateAsync(NewProvider(transports: ["RestApi"]), CancellationToken.None))).Id!.Value;

        var renamedKey = NewProvider(key: "other-key");
        var keyRefused = await Providers(db => db.UpdateAsync(id, renamedKey, CancellationToken.None));

        var changed = NewProvider(transports: ["Webhook"], isActive: false);
        changed.DisplayName = "Acme, renamed";
        var updated = await Providers(db => db.UpdateAsync(id, changed, CancellationToken.None));

        Assert.Contains("ProviderKey", keyRefused.Errors.Keys);
        Assert.True(updated.Succeeded);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var provider = check.AttendanceProviders.Include(p => p.Transports).ThenInclude(t => t.Transport).Single(p => p.Id == id);
        Assert.Equal(("Acme, renamed", false), (provider.DisplayName, provider.IsActive));
        Assert.Equal(["Webhook"], provider.Transports.Select(t => t.Transport!.TransportKey));
        Assert.True((await Providers(db => db.UpdateAsync(999, changed, CancellationToken.None))).NotFound);
        Assert.Null(await Providers(db => db.GetInputAsync(999, CancellationToken.None)));
    }

    [Fact]
    public async Task ARetiredFormat_CanNotBeChosenAnew_ButAProviderKeepsItsOwn()
    {
        var formatId = FormatId("Generic CSV");
        var id = (await Providers(db => db.CreateAsync(NewProvider(formatId: formatId), CancellationToken.None))).Id!.Value;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceFileFormats.Single(f => f.Id == formatId).IsActive = false;
            db.SaveChanges();
        }

        var keeps = await Providers(db => db.UpdateAsync(id, NewProvider(formatId: formatId), CancellationToken.None));
        var anew = await Providers(db => db.CreateAsync(NewProvider(key: "second", formatId: formatId), CancellationToken.None));

        Assert.True(keeps.Succeeded);
        Assert.Contains("DefaultFileFormatId", anew.Errors.Keys);
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
            var integration = new AttendanceIntegration { Provider = db.AttendanceProviders.Single(p => p.ProviderKey == "zkteco"), DisplayName = "HQ" };
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
        Assert.Equal((true, false), (sftp.IsPull, sftp.UsesFileFormat));

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
        Assert.Equal((true, false), (after.IsPull, after.UsesFileFormat));
        Assert.True((await Transports(db => db.UpdateAsync(999, new TransportInput { DisplayName = "x" }, CancellationToken.None))).NotFound);
    }

    [Fact]
    public async Task ASwitchedOffTransport_CanNotBeChosenForANewIntegration_ButAnExistingOneKeepsIt()
    {
        int integrationId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var biostar = db.AttendanceProviders.Single(p => p.ProviderKey == "suprema-biostar");
            var integration = new AttendanceIntegration { Provider = biostar, DisplayName = "BioStar", Transport = AttendanceTransport.RestApi };
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
        var fresh = await service.CreateAsync(new IntegrationInput { ProviderId = input.ProviderId, DisplayName = "Another", Transport = "RestApi", Status = "Active" }, CancellationToken.None);

        Assert.True(keeps.Succeeded);
        Assert.Contains("not available for new integrations", fresh.Errors[nameof(IntegrationInput.Transport)]);
    }

    private static ProviderInput NewProvider(string key = "acme-clock", int? formatId = null, string[]? transports = null, bool isActive = true) => new()
    {
        ProviderKey = key,
        DisplayName = "Acme clock",
        Manufacturer = "Acme",
        WebsiteUrl = "https://acme.example.test",
        DefaultFileFormatId = formatId,
        Transports = [.. transports ?? []],
        IsActive = isActive,
    };

    private static AttendanceAuditTrail Audit(RobeemDbContext db) => new(db, new TestUser(7), TimeProvider.System);

    private int FormatId(string name)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceFileFormats.Single(f => f.Name == name).Id;
    }

    private async Task<T> Providers<T>(Func<AttendanceProviderService, Task<T>> action, IAttendanceAdapterCatalog? adapters = null)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await action(new AttendanceProviderService(db, Audit(db), adapters ?? new StubAdapterCatalog()));
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
