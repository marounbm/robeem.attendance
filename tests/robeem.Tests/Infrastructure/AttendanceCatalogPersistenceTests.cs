using Microsoft.EntityFrameworkCore;
using robeem.DataModel;

namespace robeem.Tests.Infrastructure;

public sealed class AttendanceCatalogPersistenceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Catalog_RoundTrips_WithTenantStampedAndEnumsStoredByName()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var provider = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV", SupportedTransports = "FileUpload, Sftp" };
            var integration = new AttendanceIntegration { Provider = provider, DisplayName = "HQ", HealthStatus = AttendanceIntegrationHealth.Degraded };
            db.AttendanceDevices.Add(new AttendanceDevice
            {
                Integration = integration,
                ExternalDeviceId = "DEV-1",
                Name = "Front door",
                DeviceType = AttendanceDeviceType.RFID,
                TimeZoneId = "Asia/Beirut",
            });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var device = check.AttendanceDevices.Include(d => d.Integration).ThenInclude(i => i!.Provider).Single();
        Assert.Equal(TestDatabase.TenantA, device.TenantId);
        Assert.Equal(AttendanceDeviceType.RFID, device.DeviceType);
        Assert.Equal(AttendanceIntegrationHealth.Degraded, device.Integration!.HealthStatus);
        Assert.Equal("Generic CSV", device.Integration.Provider!.Title);
        Assert.True(device.Integration.Provider.SupportsTransport(AttendanceTransport.Sftp));
        Assert.False(device.Integration.Provider.SupportsTransport(AttendanceTransport.Webhook));

        var storedHealth = check.Database.SqlQueryRaw<string>("SELECT \"HealthStatus\" AS \"Value\" FROM \"AttendanceIntegration\"").Single();
        Assert.Equal("Degraded", storedHealth);
    }

    [Fact]
    public void Policy_AllowsOneDefaultAndOnePerSchedule()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var schedule = new TimeSchedule { Title = "Week" };
        db.AttendancePolicies.AddRange(
            new AttendancePolicy { Title = "Default" },
            new AttendancePolicy { Title = "Week", TimeSchedule = schedule });
        db.SaveChanges();

        var defaultPolicy = db.AttendancePolicies.Single(p => p.TimeScheduleId == null);
        Assert.True(defaultPolicy.IsTenantDefault);
        Assert.Equal(UnlabeledPunchInterpretationMode.Off, defaultPolicy.UnlabeledPunchInterpretationMode);
        Assert.Equal(120, defaultPolicy.DuplicateTimeWindowSeconds);

        db.AttendancePolicies.Add(new AttendancePolicy { Title = "Second default" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();

        db.AttendancePolicies.Add(new AttendancePolicy { Title = "Second for week", TimeScheduleId = schedule.Id });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }
}
