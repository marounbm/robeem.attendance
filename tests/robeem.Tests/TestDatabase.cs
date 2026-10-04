using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    public RobeemDbContext CreateContext(string? tenantId)
    {
        var options = new DbContextOptionsBuilder<RobeemDbContext>().UseSqlite(_connection).Options;
        return new RobeemDbContext(options, new FixedTenantContext(tenantId));
    }

    public void Dispose() => _connection.Dispose();
}
