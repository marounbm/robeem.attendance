using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure;
using robeem.Infrastructure.Seeding;

namespace robeem.Tests.Infrastructure.Attendance;

public sealed class AttendanceSeedTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SeedingTheCatalog_CreatesWhatRobeemOffersEveryTenant_Once()
    {
        await SeedCatalogAsync();
        await SeedCatalogAsync();

        using var db = _database.CreateCatalog();
        Assert.Equal(
            ["generic-csv", "generic-webhook", "suprema-biostar", "zkteco"],
            db.AttendanceVendors.Select(p => p.VendorKey).OrderBy(k => k).ToArray());
        Assert.Equal(
            ["generic-csv", "generic-webhook", "suprema-biostar", "zkteco"],
            db.AttendanceConnectorDefinitions.Select(c => c.ConnectorKey).OrderBy(k => k).ToArray());
        Assert.Equal(["Database", "DeviceSdk", "FileUpload", "RestApi", "Sftp", "Webhook"], db.AttendanceTransportDefinitions.Select(t => t.TransportKey).AsEnumerable().Order(StringComparer.Ordinal));
        Assert.Equal(["Biometric", "Cloud", "Facial", "Mobile", "OnPrem", "RFID"], db.AttendanceDeviceTypes.Select(t => t.Name).OrderBy(n => n).ToArray());
        Assert.Equal(["Generic CSV", "Suprema BioStar 2 export", "ZKTeco attendance export"], db.AttendancePayloadFormats.Select(f => f.Name).OrderBy(n => n).ToArray());

        // A file-based connector is read by its default payload format; an API or webhook connector by its code adapter.
        var csv = db.AttendanceConnectorDefinitions.Include(c => c.DefaultPayloadFormat).Include(c => c.Vendor).Single(c => c.ConnectorKey == "generic-csv");
        Assert.True(csv.IsActive);
        Assert.Equal(("Generic CSV", "generic-csv", null), (csv.DefaultPayloadFormat!.Name, csv.Vendor!.VendorKey, csv.AdapterKey));
        Assert.Equal("ZKTeco attendance export", db.AttendanceConnectorDefinitions.Include(c => c.DefaultPayloadFormat).Single(c => c.ConnectorKey == "zkteco").DefaultPayloadFormat!.Name);
        Assert.Null(db.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == "suprema-biostar").DefaultPayloadFormatId);

        var biostar = db.AttendanceConnectorDefinitions.Include(c => c.Transports).ThenInclude(t => t.Transport).Single(c => c.ConnectorKey == "suprema-biostar");
        Assert.Equal(["RestApi"], biostar.Transports.Select(t => t.Transport!.TransportKey));
        Assert.True(biostar.SupportsIncrementalSync);
        Assert.Equal("suprema-biostar", biostar.AdapterKey);

        var webhook = db.AttendanceConnectorDefinitions.Include(c => c.Transports).ThenInclude(t => t.Transport).Single(c => c.ConnectorKey == "generic-webhook");
        Assert.Equal(["Webhook"], webhook.Transports.Select(t => t.Transport!.TransportKey));
        Assert.False(webhook.IsActive);

        var transports = db.AttendanceTransportDefinitions.ToDictionary(t => t.TransportKey);
        Assert.Equal(("Manual file upload", false, true), (transports["FileUpload"].DisplayName, transports["FileUpload"].IsPull, transports["FileUpload"].UsesPayloadFormat));
        Assert.All(new[] { "Sftp", "RestApi", "Database", "DeviceSdk" }, key => Assert.True(transports[key].IsPull));
        Assert.False(transports["Webhook"].IsPull);
    }

    [Fact]
    public async Task SeedingATenant_GivesItTheDefaultPolicy_AndNoCatalog()
    {
        await SeedTenantAsync(TestDatabase.TenantA);
        await SeedTenantAsync(TestDatabase.TenantA);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var policy = Assert.Single(db.AttendancePolicies);
        Assert.Null(policy.TimeScheduleId);
        Assert.Equal(UnlabeledPunchInterpretationMode.Off, policy.UnlabeledPunchInterpretationMode);
        Assert.False(policy.RequiresOvertimePreApproval);
        Assert.Equal(CorrectionApprovalWorkflow.Manager, policy.CorrectionApprovalWorkflow);

        // The catalog is not a tenant's: seeding the tenant leaves it empty.
        using var catalog = _database.CreateCatalog();
        Assert.Empty(catalog.AttendanceVendors);
    }

    [Fact]
    public async Task Seed_KeepsAPlatformAdministratorsChanges_AndAddsOnlyWhatIsMissing()
    {
        await SeedCatalogAsync();
        await SeedTenantAsync(TestDatabase.TenantA);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendancePolicies.Single().RequiresOvertimePreApproval = true;
            db.SaveChanges();
        }

        using (var catalog = _database.CreateCatalog())
        {
            catalog.AttendanceVendors.Single(p => p.VendorKey == "zkteco").DisplayName = "Renamed";
            catalog.AttendanceVendors.Single(p => p.VendorKey == "zkteco").Manufacturer = string.Empty;
            catalog.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == "generic-csv").DefaultPayloadFormatId = null;

            // A connector with a transport can only go once the transport link does, and a vendor once its connector has.
            var biostar = catalog.AttendanceConnectorDefinitions.Include(c => c.Transports).Include(c => c.Vendor).Single(c => c.ConnectorKey == "suprema-biostar");
            catalog.AttendanceConnectorTransports.RemoveRange(biostar.Transports);
            catalog.AttendanceConnectorDefinitions.Remove(biostar);
            catalog.AttendanceVendors.Remove(biostar.Vendor!);
            catalog.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").DisplayName = "Secure FTP";
            catalog.AttendanceDeviceTypes.Single(t => t.Name == "RFID").IsActive = false;
            catalog.AttendancePayloadFormats.Single(f => f.Name == "ZKTeco attendance export").PayloadColumns = "Name";
            catalog.SaveChanges();
        }

        await SeedCatalogAsync();
        await SeedTenantAsync(TestDatabase.TenantA);

        using var checkTenant = _database.CreateContext(TestDatabase.TenantA);
        Assert.True(checkTenant.AttendancePolicies.Single().RequiresOvertimePreApproval);
        using var check = _database.CreateCatalog();
        var zk = check.AttendanceVendors.Single(p => p.VendorKey == "zkteco");
        Assert.Equal(("Renamed", string.Empty), (zk.DisplayName, zk.Manufacturer));
        Assert.Equal(4, check.AttendanceVendors.Count());
        Assert.Equal(4, check.AttendanceConnectorDefinitions.Count());

        // The cleared default stays cleared: the format exists, so it is not linked again.
        Assert.Null(check.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == "generic-csv").DefaultPayloadFormatId);
        Assert.Equal("Secure FTP", check.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").DisplayName);
        Assert.False(check.AttendanceDeviceTypes.Single(t => t.Name == "RFID").IsActive);
        Assert.Equal("Name", check.AttendancePayloadFormats.Single(f => f.Name == "ZKTeco attendance export").PayloadColumns);
        Assert.Equal(3, check.AttendancePayloadFormats.Count());
    }

    [Fact]
    public async Task Seed_NeverPutsBackATransportOrDeviceTypeAPlatformAdministratorChanged()
    {
        await SeedCatalogAsync();
        using (var catalog = _database.CreateCatalog())
        {
            var connector = AttendanceTestData.Connector("old-vendor", "Old vendor").WithTransport(catalog.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp"));
            catalog.AttendanceConnectorDefinitions.Add(connector);
            catalog.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = "Palm reader", IsActive = true });
            catalog.SaveChanges();
        }

        await SeedCatalogAsync();

        using (var catalog = _database.CreateCatalog())
        {
            var connector = catalog.AttendanceConnectorDefinitions.Include(c => c.Transports).ThenInclude(t => t.Transport).Single(c => c.ConnectorKey == "old-vendor");
            Assert.Equal(["Sftp"], connector.Transports.Select(t => t.Transport!.TransportKey));

            // The administrator then takes the transport away and retires a type.
            catalog.AttendanceConnectorTransports.RemoveRange(connector.Transports);
            catalog.AttendanceDeviceTypes.Single(t => t.Name == "Facial").IsActive = false;
            catalog.SaveChanges();
        }

        await SeedCatalogAsync();

        using var check = _database.CreateCatalog();
        Assert.Empty(check.AttendanceConnectorDefinitions.Include(c => c.Transports).Single(c => c.ConnectorKey == "old-vendor").Transports);
        Assert.False(check.AttendanceDeviceTypes.Single(t => t.Name == "Facial").IsActive);
        Assert.Equal(7, check.AttendanceDeviceTypes.Count());
    }

    [Fact]
    public async Task Seed_LinksABuiltInFormatToItsConnector_TheRunThatCreatesIt_AndLeavesTheLinkAloneAfterwards()
    {
        using (var catalog = _database.CreateCatalog())
        {
            // A catalog from before the formats were data: a ZKTeco connector with nothing to read it by.
            catalog.AttendanceConnectorDefinitions.Add(AttendanceTestData.Connector("zkteco", "ZKTeco"));
            catalog.SaveChanges();
        }

        await SeedCatalogAsync();

        using (var catalog = _database.CreateCatalog())
        {
            Assert.Equal("ZKTeco attendance export", catalog.AttendanceConnectorDefinitions.Include(c => c.DefaultPayloadFormat).Single(c => c.ConnectorKey == "zkteco").DefaultPayloadFormat!.Name);

            // An administrator chooses another format for it.
            catalog.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == "zkteco").DefaultPayloadFormat = catalog.AttendancePayloadFormats.Single(f => f.Name == "Generic CSV");
            catalog.SaveChanges();
        }

        await SeedCatalogAsync();

        using var check = _database.CreateCatalog();
        Assert.Equal("Generic CSV", check.AttendanceConnectorDefinitions.Include(c => c.DefaultPayloadFormat).Single(c => c.ConnectorKey == "zkteco").DefaultPayloadFormat!.Name);
    }

    [Fact]
    public async Task Seed_GivesEachTenantItsOwnPolicy_AndTheCatalogIsNotCopiedToThem()
    {
        await SeedCatalogAsync();
        await SeedTenantAsync(TestDatabase.TenantA);
        await SeedTenantAsync(TestDatabase.TenantB);

        foreach (var tenant in new[] { TestDatabase.TenantA, TestDatabase.TenantB })
        {
            using var db = _database.CreateContext(tenant);
            Assert.Single(db.AttendancePolicies);
        }

        using var catalog = _database.CreateCatalog();
        Assert.Equal(4, catalog.AttendanceVendors.Count());
        Assert.Equal(6, catalog.AttendanceTransportDefinitions.Count());
        Assert.Equal(3, catalog.AttendancePayloadFormats.Count());
    }

    [Fact]
    public async Task OnlyThePrimeTenant_CanSeedTheCatalog()
    {
        using var catalog = _database.CreateCatalog(TestDatabase.TenantA);

        await Assert.ThrowsAsync<CatalogReadOnlyException>(() =>
            new CatalogSeedRunner(catalog, [new CatalogSeedContributor()], NullLogger<CatalogSeedRunner>.Instance).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EveryConnectorSeededActive_IsUsable_ByAFormatOrByARegisteredAdapter()
    {
        await SeedCatalogAsync();
        using var services = new ServiceCollection().AddRobeemInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var adapters = services.GetRequiredService<IAttendanceAdapterCatalog>();

        using var db = _database.CreateCatalog();
        var active = db.AttendanceConnectorDefinitions.Include(c => c.Vendor).Where(c => c.IsActive).ToList();

        Assert.Equal(["generic-csv", "suprema-biostar", "zkteco"], active.Select(c => c.ConnectorKey).Order());
        Assert.All(active, c => Assert.True(ConnectorUsability.IsUsable(c, c.Vendor!.IsActive, adapters), c.ConnectorKey));
    }

    [Fact]
    public void TheAdapterCatalog_KnowsTheCodeAdapters_AndNotTheVendorsThatAreFormats()
    {
        using var services = new ServiceCollection().AddRobeemInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var adapters = services.GetRequiredService<IAttendanceAdapterCatalog>();

        Assert.True(adapters.IsRegistered("suprema-biostar"));
        Assert.True(adapters.IsRegistered("generic-webhook"));
        Assert.False(adapters.IsRegistered("zkteco"));
        Assert.False(adapters.IsRegistered("generic-csv"));
        Assert.False(adapters.IsRegistered("something-new"));
    }

    private async Task SeedCatalogAsync()
    {
        using var catalog = _database.CreateCatalog();
        await new CatalogSeedRunner(catalog, [new CatalogSeedContributor()], NullLogger<CatalogSeedRunner>.Instance).RunAsync(CancellationToken.None);
    }

    private async Task SeedTenantAsync(string tenant)
    {
        using var db = _database.CreateContext(tenant);
        await new SeedDataRunner(db, new ISeedContributor[] { new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
            .RunAsync(CancellationToken.None);
    }
}
