using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Seeding;
using robeem.DataModel;
using robeem.Infrastructure;
using robeem.Infrastructure.Attendance.Providers;
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

        var csv = db.AttendanceProviders.Single(p => p.ProviderKey == "generic-csv");
        Assert.True(csv.IsActive);
        Assert.True(csv.SupportsTransport(AttendanceTransport.FileUpload));

        var webhook = db.AttendanceProviders.Single(p => p.ProviderKey == "generic-webhook");
        Assert.True(webhook.SupportsWebhooks);
        Assert.False(webhook.IsActive);

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
            db.AttendanceProviders.Remove(db.AttendanceProviders.Single(p => p.ProviderKey == "suprema-biostar"));
            db.SaveChanges();
        }

        await SeedAsync(TestDatabase.TenantA);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.True(check.AttendancePolicies.Single().RequiresOvertimePreApproval);
        Assert.Equal("Renamed", check.AttendanceProviders.Single(p => p.ProviderKey == "zkteco").DisplayName);
        Assert.Equal(4, check.AttendanceProviders.Count());
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
            Assert.Single(db.AttendancePolicies);
        }
    }

    [Fact]
    public void TheCsvKey_IsTheAdaptersKey()
    {
        Assert.Equal(GenericCsvProvider.ProviderKey, AttendanceSeedContributor.CsvProviderKey);
    }

    [Fact]
    public async Task NoProviderIsSeededActive_WithoutARegisteredAdapterAndInterpreter()
    {
        await SeedAsync(TestDatabase.TenantA);
        using var services = new ServiceCollection().AddRobeemInfrastructure(new ConfigurationBuilder().Build()).BuildServiceProvider();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var active = db.AttendanceProviders.Where(p => p.IsActive).ToList();

        Assert.NotEmpty(active);
        Assert.All(active, p =>
        {
            Assert.NotNull(services.GetKeyedService<IAttendanceProvider>(p.ProviderKey));
            Assert.NotNull(services.GetKeyedService<IAttendanceEventInterpreter>(p.ProviderKey));
        });
    }

    private async Task SeedAsync(string tenant)
    {
        using var db = _database.CreateContext(tenant);
        await new SeedDataRunner(db, new ISeedContributor[] { new AttendanceSeedContributor() }, NullLogger<SeedDataRunner>.Instance)
            .RunAsync(CancellationToken.None);
    }
}
