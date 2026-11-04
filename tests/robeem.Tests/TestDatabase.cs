using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using robeem.Application.Attendance;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Attendance.Seeding;
using robeem.Application.Tenancy;
using robeem.Infrastructure.Persistence;
using robeem.Infrastructure.Persistence.Catalog;
using robeem.Infrastructure.Tenancy;

namespace robeem.Tests;

/// <summary>
/// One in-memory SQLite database shared by every tenant context created from it. In production each
/// tenant has its own database; sharing one here lets tests prove the TenantId column and query
/// filter isolate tenants even if two tenants ever ended up in the same database.
/// The attendance catalog is a database of its own, as it is in production (the prime tenant's): a second in-memory
/// SQLite connection, read by every tenant and writable only as the prime tenant.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    public const string TenantA = "tenant-a";
    public const string TenantB = "tenant-b";

    /// <summary>The tenant that owns the catalog in tests.</summary>
    public const string Prime = "prime";

    private readonly SqliteConnection _connection;
    private readonly SqliteConnection _catalogConnection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _catalogConnection = new SqliteConnection("DataSource=:memory:");
        _catalogConnection.Open();

        using var db = CreateContext(TenantA);
        db.Database.EnsureCreated();
        using var catalog = CreateCatalog();
        catalog.Database.EnsureCreated();
    }

    public RobeemDbContext CreateContext(string? tenantId, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RobeemDbContext>().UseSqlite(_connection)
            .AddInterceptors([UtcDateTimeGuardInterceptor.Instance, .. interceptors])
            .Options;
        return new RobeemDbContext(options, new FixedTenantContext(tenantId));
    }

    /// <summary>The catalog, as a scope of the given tenant sees it: writable only when that tenant is <see cref="Prime"/>.</summary>
    public CatalogDbContext CreateCatalog(string tenantId = Prime)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(_catalogConnection)
            .AddInterceptors(UtcDateTimeGuardInterceptor.Instance)
            .Options;
        return new CatalogDbContext(options, new FixedPrimeTenantContext(tenantId));
    }

    /// <summary>A reader of the catalog, as tenant code (ingestion, normalization, integrations) sees it. No code adapter is registered unless the adapter catalog says so.</summary>
    public CatalogReader CreateCatalogReader(IAttendanceAdapterCatalog? adapters = null, IPayloadDecryptorCatalog? decryptors = null) =>
        new(CreateCatalog(), adapters ?? new StubAdapterCatalog(), decryptors ?? new PayloadDecryptorCatalog([]));

    /// <summary>
    /// Gives the catalog what startup gives the prime tenant's: transports, device types, the built-in payload formats
    /// (generic CSV, ZKTeco, BioStar) and the vendors, each file-based vendor linked to its default format.
    /// </summary>
    public void SeedAttendanceCatalog()
    {
        using var catalog = CreateCatalog();
        new CatalogSeedContributor().SeedAsync(catalog, CancellationToken.None).GetAwaiter().GetResult();
        catalog.SaveChanges();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _catalogConnection.Dispose();
    }
}
