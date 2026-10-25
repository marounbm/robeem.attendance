using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
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
    public async Task Seed_CreatesTheCatalogAndTheDefaultPolicy_Once()
    {
        await SeedAsync(TestDatabase.TenantA);
        await SeedAsync(TestDatabase.TenantA);

        using var db = _database.CreateContext(TestDatabase.TenantA);
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

        var policy = Assert.Single(db.AttendancePolicies);
        Assert.Null(policy.TimeScheduleId);
        Assert.Equal(UnlabeledPunchInterpretationMode.Off, policy.UnlabeledPunchInterpretationMode);
        Assert.False(policy.RequiresOvertimePreApproval);
        Assert.Equal(CorrectionApprovalWorkflow.Manager, policy.CorrectionApprovalWorkflow);
    }

    [Fact]
    public async Task Seed_KeepsAnAdministratorsChanges_AndAddsOnlyWhatIsMissing()
    {
        await SeedAsync(TestDatabase.TenantA);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendancePolicies.Single().RequiresOvertimePreApproval = true;
            db.AttendanceVendors.Single(p => p.VendorKey == "zkteco").DisplayName = "Renamed";
            db.AttendanceVendors.Single(p => p.VendorKey == "zkteco").Manufacturer = string.Empty;
            db.AttendanceConnectorDefinitions.Single(c => c.ConnectorKey == "generic-csv").DefaultPayloadFormatId = null;

            // A connector with a transport can only go once the transport link does, and a vendor once its connector has.
            var biostar = db.AttendanceConnectorDefinitions.Include(c => c.Transports).Include(c => c.Vendor).Single(c => c.ConnectorKey == "suprema-biostar");
            db.AttendanceConnectorTransports.RemoveRange(biostar.Transports);
            db.AttendanceConnectorDefinitions.Remove(biostar);
            db.AttendanceVendors.Remove(biostar.Vendor!);
            db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").DisplayName = "Secure FTP";
            db.AttendanceDeviceTypes.Single(t => t.Name == "RFID").IsActive = false;
            db.AttendancePayloadFormats.Single(f => f.Name == "ZKTeco attendance export").PayloadColumns = "Name";
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.True(check.AttendancePolicies.Single().RequiresOvertimePreApproval);
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
    public async Task Seed_NeverPutsBackATransportOrDeviceTypeAnAdministratorChanged()
    {
        await SeedAsync(TestDatabase.TenantA);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var connector = AttendanceTestData.Connector("old-vendor", "Old vendor").WithTransport(db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp"));
            var integration = new AttendanceIntegration { Connector = connector, DisplayName = "Old clock" };
            db.AttendanceDevices.AddRange(
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "A", Name = "Known type", TimeZoneId = "UTC", DeviceType = db.AttendanceDeviceTypes.Single(t => t.Name == "Facial") },
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "B", Name = "Unlisted type", TimeZoneId = "UTC", DeviceType = new AttendanceDeviceType { Name = "Palm reader", IsActive = true } });
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var connector = db.AttendanceConnectorDefinitions.Include(c => c.Transports).ThenInclude(t => t.Transport).Single(c => c.ConnectorKey == "old-vendor");
            Assert.Equal(["Sftp"], connector.Transports.Select(t => t.Transport!.TransportKey));

            var devices = db.AttendanceDevices.Include(d => d.DeviceType).ToDictionary(d => d.ExternalDeviceId);
            Assert.Equal("Facial", devices["A"].DeviceType!.Name);
            Assert.Equal("Palm reader", devices["B"].DeviceType!.Name);

            // An administrator then takes the transport away and changes a device's type.
            db.AttendanceConnectorTransports.RemoveRange(connector.Transports);
            devices["A"].DeviceType = db.AttendanceDeviceTypes.Single(t => t.Name == "Mobile");
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceConnectorDefinitions.Include(c => c.Transports).Single(c => c.ConnectorKey == "old-vendor").Transports);
        Assert.Equal("Mobile", check.AttendanceDevices.Include(d => d.DeviceType).Single(d => d.ExternalDeviceId == "A").DeviceType!.Name);
        Assert.Equal(7, check.AttendanceDeviceTypes.Count());
    }

    [Fact]
    public async Task Seed_LinksTheBuiltInFormatsToTheirConnectorsAndUploadIntegrations_TheRunThatCreatesThem()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            // A database from before the formats were data: ZKTeco and generic CSV connectors, read in code, with their integrations.
            var zk = AttendanceTestData.Connector("zkteco", "ZKTeco");
            var hasOwnFormat = new AttendancePayloadFormat { Name = "Mine", EmployeeColumn = "1", TimestampColumn = "2", TimestampFormats = "dd/MM/yyyy HH:mm", HasHeaderRow = false };
            db.AttendanceIntegrations.AddRange(
                new AttendanceIntegration { Connector = zk, DisplayName = "Uploads" },
                new AttendanceIntegration { Connector = zk, DisplayName = "Own format", PayloadFormat = hasOwnFormat },
                new AttendanceIntegration { Connector = zk, DisplayName = "Pulled" }.WithConnection(AttendanceTransport.RestApi),
                new AttendanceIntegration { Connector = AttendanceTestData.Connector("generic-csv", "Generic CSV"), DisplayName = "CSV uploads" });
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var byName = db.AttendanceIntegrations.Include(i => i.PayloadFormat).ToDictionary(i => i.DisplayName);
            Assert.Equal("ZKTeco attendance export", byName["Uploads"].PayloadFormat!.Name);
            Assert.Equal("Mine", byName["Own format"].PayloadFormat!.Name);
            Assert.Null(byName["Pulled"].PayloadFormatId);
            Assert.Equal("Generic CSV", byName["CSV uploads"].PayloadFormat!.Name);
            Assert.Equal("ZKTeco attendance export", db.AttendanceConnectorDefinitions.Include(c => c.DefaultPayloadFormat).Single(c => c.ConnectorKey == "zkteco").DefaultPayloadFormat!.Name);

            // An administrator chooses another format for one integration.
            byName["Uploads"].PayloadFormat = db.AttendancePayloadFormats.Single(f => f.Name == "Mine");
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Mine", check.AttendanceIntegrations.Include(i => i.PayloadFormat).Single(i => i.DisplayName == "Uploads").PayloadFormat!.Name);
    }

    [Fact]
    public async Task Seed_FillsEachTenantOnItsOwn()
    {
        await SeedAsync(TestDatabase.TenantA);
        await SeedAsync(TestDatabase.TenantB);

        foreach (var tenant in new[] { TestDatabase.TenantA, TestDatabase.TenantB })
        {
            using var db = _database.CreateContext(tenant);
            Assert.Equal(4, db.AttendanceVendors.Count());
            Assert.Equal(4, db.AttendanceConnectorDefinitions.Count());
            Assert.Equal(6, db.AttendanceTransportDefinitions.Count());
            Assert.Equal(3, db.AttendancePayloadFormats.Count());
            Assert.Single(db.AttendancePolicies);
        }
    }

    [Fact]
    public async Task EveryConnectorSeededActive_IsUsable_ByAFormatOrByARegisteredAdapter()
    {
        await SeedAsync(TestDatabase.TenantA);
        using var services = new ServiceCollection().AddRobeemInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var adapters = services.GetRequiredService<IAttendanceAdapterCatalog>();

        using var db = _database.CreateContext(TestDatabase.TenantA);
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

    private async Task SeedAsync(string tenant)
    {
        using var db = _database.CreateContext(tenant);
        await new SeedDataRunner(db, new ISeedContributor[] { new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
            .RunAsync(CancellationToken.None);
    }
}
