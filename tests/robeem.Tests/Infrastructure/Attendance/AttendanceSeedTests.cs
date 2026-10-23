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
            db.AttendanceProviders.Select(p => p.ProviderKey).OrderBy(k => k).ToArray());
        Assert.Equal(["Database", "DeviceSdk", "FileUpload", "RestApi", "Sftp", "Webhook"], db.AttendanceTransportDefinitions.Select(t => t.TransportKey).AsEnumerable().Order(StringComparer.Ordinal));
        Assert.Equal(["Biometric", "Cloud", "Facial", "Mobile", "OnPrem", "RFID"], db.AttendanceDeviceTypes.Select(t => t.Name).OrderBy(n => n).ToArray());
        Assert.Equal(["Generic CSV", "Suprema BioStar 2 export", "ZKTeco attendance export"], db.AttendanceFileFormats.Select(f => f.Name).OrderBy(n => n).ToArray());

        // A file-based vendor is read by its default format; an API or webhook vendor by its code adapter.
        var csv = db.AttendanceProviders.Include(p => p.DefaultFileFormat).Single(p => p.ProviderKey == "generic-csv");
        Assert.True(csv.IsActive);
        Assert.Equal("Generic CSV", csv.DefaultFileFormat!.Name);
        Assert.Equal("ZKTeco attendance export", db.AttendanceProviders.Include(p => p.DefaultFileFormat).Single(p => p.ProviderKey == "zkteco").DefaultFileFormat!.Name);
        Assert.Null(db.AttendanceProviders.Single(p => p.ProviderKey == "suprema-biostar").DefaultFileFormatId);

        var biostar = db.AttendanceProviders.Include(p => p.Transports).ThenInclude(t => t.Transport).Single(p => p.ProviderKey == "suprema-biostar");
        Assert.Equal(["RestApi"], biostar.Transports.Select(t => t.Transport!.TransportKey));
        Assert.True(biostar.SupportsIncrementalSync);

        var webhook = db.AttendanceProviders.Single(p => p.ProviderKey == "generic-webhook");
        Assert.True(webhook.SupportsWebhooks);
        Assert.False(webhook.IsActive);

        var transports = db.AttendanceTransportDefinitions.ToDictionary(t => t.TransportKey);
        Assert.Equal(("Manual file upload", false, true), (transports["FileUpload"].DisplayName, transports["FileUpload"].IsPull, transports["FileUpload"].UsesFileFormat));
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
            db.AttendanceProviders.Single(p => p.ProviderKey == "zkteco").DisplayName = "Renamed";
            db.AttendanceProviders.Single(p => p.ProviderKey == "zkteco").Manufacturer = string.Empty;
            db.AttendanceProviders.Single(p => p.ProviderKey == "generic-csv").DefaultFileFormatId = null;

            // A provider with a transport can only go once the transport link does.
            var biostar = db.AttendanceProviders.Include(p => p.Transports).Single(p => p.ProviderKey == "suprema-biostar");
            db.AttendanceProviderTransports.RemoveRange(biostar.Transports);
            db.AttendanceProviders.Remove(biostar);
            db.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").DisplayName = "Secure FTP";
            db.AttendanceDeviceTypes.Single(t => t.Name == "RFID").IsActive = false;
            db.AttendanceFileFormats.Single(f => f.Name == "ZKTeco attendance export").PayloadColumns = "Name";
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.True(check.AttendancePolicies.Single().RequiresOvertimePreApproval);
        var zk = check.AttendanceProviders.Single(p => p.ProviderKey == "zkteco");
        Assert.Equal(("Renamed", string.Empty), (zk.DisplayName, zk.Manufacturer));
        Assert.Equal(4, check.AttendanceProviders.Count());

        // The cleared default stays cleared: the format exists, so it is not linked again.
        Assert.Null(check.AttendanceProviders.Single(p => p.ProviderKey == "generic-csv").DefaultFileFormatId);
        Assert.Equal("Secure FTP", check.AttendanceTransportDefinitions.Single(t => t.TransportKey == "Sftp").DisplayName);
        Assert.False(check.AttendanceDeviceTypes.Single(t => t.Name == "RFID").IsActive);
        Assert.Equal("Name", check.AttendanceFileFormats.Single(f => f.Name == "ZKTeco attendance export").PayloadColumns);
        Assert.Equal(3, check.AttendanceFileFormats.Count());
    }

    [Fact]
    public async Task Seed_ReadsTransportsAndDeviceTypesFromTheTextTheyWereOnceStoredAs_AndThenLetsGo()
    {
        await SeedAsync(TestDatabase.TenantA);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            // As a database from before the catalog was a table has them: text, and no rows in the new tables.
            var provider = new AttendanceProvider { ProviderKey = "old-vendor", DisplayName = "Old vendor", LegacySupportedTransports = "FileUpload, Sftp,Carrier pigeon" };
            var integration = new AttendanceIntegration { Provider = provider, DisplayName = "Old clock" };
            db.AttendanceDevices.AddRange(
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "A", Name = "Known type", TimeZoneId = "UTC", LegacyDeviceType = "Facial" },
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "B", Name = "Unlisted type", TimeZoneId = "UTC", LegacyDeviceType = "Palm reader" });
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var provider = db.AttendanceProviders.Include(p => p.Transports).ThenInclude(t => t.Transport).Single(p => p.ProviderKey == "old-vendor");
            Assert.Equal(["Sftp"], provider.Transports.Select(t => t.Transport!.TransportKey));
            Assert.Equal(string.Empty, provider.LegacySupportedTransports);

            var devices = db.AttendanceDevices.Include(d => d.DeviceType).ToDictionary(d => d.ExternalDeviceId);
            Assert.Equal("Facial", devices["A"].DeviceType!.Name);
            Assert.Equal("Palm reader", devices["B"].DeviceType!.Name);
            Assert.True(devices["B"].DeviceType!.IsActive);
            Assert.All(devices.Values, d => Assert.Equal(string.Empty, d.LegacyDeviceType));

            // An administrator then takes the transport away and changes a device's type.
            db.AttendanceProviderTransports.RemoveRange(provider.Transports);
            devices["A"].DeviceType = db.AttendanceDeviceTypes.Single(t => t.Name == "Mobile");
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.AttendanceProviders.Include(p => p.Transports).Single(p => p.ProviderKey == "old-vendor").Transports);
        Assert.Equal("Mobile", check.AttendanceDevices.Include(d => d.DeviceType).Single(d => d.ExternalDeviceId == "A").DeviceType!.Name);
        Assert.Equal(7, check.AttendanceDeviceTypes.Count());
    }

    [Fact]
    public async Task Seed_LinksTheBuiltInFormatsToTheirProvidersAndUploadIntegrations_TheRunThatCreatesThem()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            // A database from before the formats were data: ZKTeco and generic CSV providers, read in code, with their integrations.
            var zk = new AttendanceProvider { ProviderKey = "zkteco", DisplayName = "ZKTeco" };
            var hasOwnFormat = new AttendanceFileFormat { Name = "Mine", EmployeeColumn = "1", TimestampColumn = "2", TimestampFormats = "dd/MM/yyyy HH:mm", HasHeaderRow = false };
            db.AttendanceIntegrations.AddRange(
                new AttendanceIntegration { Provider = zk, DisplayName = "Uploads" },
                new AttendanceIntegration { Provider = zk, DisplayName = "Own format", FileFormat = hasOwnFormat },
                new AttendanceIntegration { Provider = zk, DisplayName = "Pulled", Transport = AttendanceTransport.RestApi },
                new AttendanceIntegration { Provider = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV" }, DisplayName = "CSV uploads" });
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var byName = db.AttendanceIntegrations.Include(i => i.FileFormat).ToDictionary(i => i.DisplayName);
            Assert.Equal("ZKTeco attendance export", byName["Uploads"].FileFormat!.Name);
            Assert.Equal("Mine", byName["Own format"].FileFormat!.Name);
            Assert.Null(byName["Pulled"].FileFormatId);
            Assert.Equal("Generic CSV", byName["CSV uploads"].FileFormat!.Name);
            Assert.Equal("ZKTeco attendance export", db.AttendanceProviders.Include(p => p.DefaultFileFormat).Single(p => p.ProviderKey == "zkteco").DefaultFileFormat!.Name);

            // An administrator chooses another format for one integration.
            byName["Uploads"].FileFormat = db.AttendanceFileFormats.Single(f => f.Name == "Mine");
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal("Mine", check.AttendanceIntegrations.Include(i => i.FileFormat).Single(i => i.DisplayName == "Uploads").FileFormat!.Name);
    }

    [Fact]
    public async Task Seed_FillsEachTenantOnItsOwn()
    {
        await SeedAsync(TestDatabase.TenantA);
        await SeedAsync(TestDatabase.TenantB);

        foreach (var tenant in new[] { TestDatabase.TenantA, TestDatabase.TenantB })
        {
            using var db = _database.CreateContext(tenant);
            Assert.Equal(4, db.AttendanceProviders.Count());
            Assert.Equal(6, db.AttendanceTransportDefinitions.Count());
            Assert.Equal(3, db.AttendanceFileFormats.Count());
            Assert.Single(db.AttendancePolicies);
        }
    }

    [Fact]
    public async Task EveryProviderSeededActive_IsUsable_ByAFormatOrByARegisteredAdapter()
    {
        await SeedAsync(TestDatabase.TenantA);
        using var services = new ServiceCollection().AddRobeemInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();
        var adapters = services.GetRequiredService<IAttendanceAdapterCatalog>();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var active = db.AttendanceProviders.Where(p => p.IsActive).ToList();

        Assert.Equal(["generic-csv", "suprema-biostar", "zkteco"], active.Select(p => p.ProviderKey).Order());
        Assert.All(active, p => Assert.True(ProviderUsability.IsUsable(p, adapters), p.ProviderKey));
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
