using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests;

/// <summary>
/// One in-memory SQLite database shared by every context created from it. In production each
/// tenant has its own database; sharing one here lets tests prove the TenantId column and query
/// filter isolate tenants even if two tenants ever ended up in the same database.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = CreateContext(TenantA);
        db.Database.EnsureCreated();
    }

    public RobeemDbContext CreateContext(string? tenantId, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RobeemDbContext>().UseSqlite(_connection)
            .AddInterceptors([UtcDateTimeGuardInterceptor.Instance, .. interceptors])
            .Options;
        return new RobeemDbContext(options, new FixedTenantContext(tenantId));
    }

    /// <summary>
    /// Gives a tenant the attendance catalog startup gives it: transports, device types, the built-in file formats
    /// (generic CSV, ZKTeco) and the providers, each file-based provider linked to its default format. Not the default
    /// attendance policy: a test that wants one adds its own.
    /// </summary>
    public void SeedAttendanceCatalog(string tenantId = TenantA)
    {
        using var db = CreateContext(tenantId);
        new AttendanceSeedContributor().SeedAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        db.SaveChanges();
        db.AttendancePolicies.RemoveRange(db.AttendancePolicies.Where(p => p.TimeScheduleId == null));
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();
}
