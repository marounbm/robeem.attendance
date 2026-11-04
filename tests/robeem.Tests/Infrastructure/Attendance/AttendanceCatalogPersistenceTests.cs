using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance.Catalog;
using robeem.DataModel;

namespace robeem.Tests.Infrastructure.Attendance;

public sealed class AttendanceCatalogPersistenceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Catalog_RoundTrips_InItsOwnDatabase_WithNoTenantColumnAndEnumsStoredByName()
    {
        using (var catalog = _database.CreateCatalog())
        {
            var connector = AttendanceTestData.Connector("generic-csv", "Generic CSV")
                .WithTransport(new AttendanceTransportDefinition { TransportKey = "Sftp", DisplayName = "SFTP drop", IsPull = true });
            catalog.AttendanceConnectorDefinitions.Add(connector);
            catalog.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = "RFID" });
            catalog.SaveChanges();
        }

        using var check = _database.CreateCatalog();
        var connectorBack = check.AttendanceConnectorDefinitions.Include(c => c.Vendor).Include(c => c.Transports).ThenInclude(t => t.Transport).Single();
        Assert.Equal("Generic CSV", connectorBack.Title);
        Assert.Equal("generic-csv", connectorBack.Vendor!.VendorKey);
        Assert.Equal("Sftp", Assert.Single(connectorBack.Transports).Transport!.TransportKey);
        Assert.Equal("None", check.Database.SqlQueryRaw<string>("SELECT \"EncryptionKind\" AS \"Value\" FROM \"AttendanceConnectorDefinition\"").Single());

        // The catalog is the same for every tenant: its tables have no tenant column.
        Assert.Empty(check.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('AttendanceDeviceType') WHERE name = 'TenantId'"));
    }

    [Fact]
    public void ATenantsDataRefersToTheCatalogByKey_AndTheTenantsDatabaseHoldsNoCatalogTable()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var integration = new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = "HQ", HealthStatus = AttendanceIntegrationHealth.Degraded };
            db.AttendanceDevices.Add(new AttendanceDevice
            {
                Integration = integration,
                ExternalDeviceId = "DEV-1",
                Name = "Front door",
                DeviceTypeName = "RFID",
                TimeZoneId = "Asia/Beirut",
            });
            db.SaveChanges();
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var device = check.AttendanceDevices.Include(d => d.Integration).Single();
        Assert.Equal(TestDatabase.TenantA, device.TenantId);
        Assert.Equal("RFID", device.DeviceTypeName);
        Assert.Equal(("generic-csv", AttendanceIntegrationHealth.Degraded), (device.Integration!.ConnectorKey, device.Integration.HealthStatus));

        var storedHealth = check.Database.SqlQueryRaw<string>("SELECT \"HealthStatus\" AS \"Value\" FROM \"AttendanceIntegration\"").Single();
        Assert.Equal("Degraded", storedHealth);

        // No catalog table in the tenant's database, and no foreign key from tenant data into one.
        var tables = check.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'table'").ToList();
        Assert.DoesNotContain(tables, t => t is "AttendanceVendor" or "AttendanceConnectorDefinition" or "AttendancePayloadFormat" or "AttendanceDeviceType" or "AttendanceTransportDefinition");
    }

    [Fact]
    public void NoTenantEntity_HasAForeignKeyOrANavigationToTheCatalog()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var catalogTypes = new[]
        {
            typeof(AttendanceVendor), typeof(AttendanceConnectorDefinition), typeof(AttendanceConnectorTransport), typeof(AttendanceTransportDefinition),
            typeof(AttendanceDeviceType), typeof(AttendancePayloadFormat), typeof(AttendancePayloadEventCode),
        };

        Assert.All(catalogTypes, type => Assert.Null(db.Model.FindEntityType(type)));
    }

    [Fact]
    public void OnlyThePrimeTenant_CanSaveTheCatalog_AnyOtherTenantCanOnlyRead()
    {
        using (var prime = _database.CreateCatalog())
        {
            prime.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = "RFID" });
            prime.SaveChanges();
        }

        using var tenant = _database.CreateCatalog(TestDatabase.TenantA);
        Assert.Equal("RFID", Assert.Single(tenant.AttendanceDeviceTypes).Name);

        tenant.AttendanceDeviceTypes.Add(new AttendanceDeviceType { Name = "Palm reader" });
        Assert.Throws<CatalogReadOnlyException>(() => tenant.SaveChanges());
        tenant.ChangeTracker.Clear();

        tenant.AttendanceDeviceTypes.Single().IsActive = false;
        Assert.Throws<CatalogReadOnlyException>(() => tenant.SaveChanges());
        tenant.ChangeTracker.Clear();

        tenant.AttendanceDeviceTypes.Remove(tenant.AttendanceDeviceTypes.Single());
        Assert.Throws<CatalogReadOnlyException>(() => tenant.SaveChanges());

        using var check = _database.CreateCatalog();
        var type = Assert.Single(check.AttendanceDeviceTypes);
        Assert.Equal(("RFID", true), (type.Name, type.IsActive));
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
