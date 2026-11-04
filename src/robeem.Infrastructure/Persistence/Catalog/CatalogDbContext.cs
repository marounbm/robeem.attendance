using Microsoft.EntityFrameworkCore;
using robeem.Application.Attendance.Catalog;
using robeem.Application.Persistence;
using robeem.Application.Tenancy;
using robeem.DataModel;

namespace robeem.Infrastructure.Persistence.Catalog;

/// <summary>
/// The attendance catalog, in the prime tenant's database (schema <c>Catalog</c>). It is platform data, the same for every tenant,
/// so it has no <c>TenantId</c> and no query filter: every tenant's scope reads it. Writing is the prime tenant's alone: a save
/// from any other tenant's scope throws <see cref="CatalogReadOnlyException"/>, whatever the caller's role.
/// </summary>
public class CatalogDbContext : DbContext, ICatalogDbContext
{
    /// <summary>The name of the table that records which catalog migrations were applied; apart from the tenant migrations' table, which lives in the same database.</summary>
    public const string MigrationsHistoryTable = "__CatalogMigrationsHistory";

    private readonly IPrimeTenantContext _prime;

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options, IPrimeTenantContext prime)
        : base(options)
    {
        _prime = prime;
    }

    public DbSet<AttendanceVendor> AttendanceVendors => Set<AttendanceVendor>();

    public DbSet<AttendanceConnectorDefinition> AttendanceConnectorDefinitions => Set<AttendanceConnectorDefinition>();

    public DbSet<AttendanceConnectorTransport> AttendanceConnectorTransports => Set<AttendanceConnectorTransport>();

    public DbSet<AttendanceTransportDefinition> AttendanceTransportDefinitions => Set<AttendanceTransportDefinition>();

    public DbSet<AttendanceDeviceType> AttendanceDeviceTypes => Set<AttendanceDeviceType>();

    public DbSet<AttendancePayloadFormat> AttendancePayloadFormats => Set<AttendancePayloadFormat>();

    public DbSet<AttendancePayloadEventCode> AttendancePayloadEventCodes => Set<AttendancePayloadEventCode>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly, PersistenceConventions.IsCatalogType);

        PersistenceConventions.Apply(modelBuilder, Database.IsNpgsql());
    }

    private void ApplyWriteRules()
    {
        var changes = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (changes.Count == 0)
        {
            return;
        }

        if (!_prime.IsCurrentTenantPrime)
        {
            throw new CatalogReadOnlyException();
        }

        foreach (var entry in changes.Where(e => e.State == EntityState.Modified && e.Metadata.FindProperty(nameof(Entity.ModifiedOn)) is not null))
        {
            entry.Property(nameof(Entity.ModifiedOn)).CurrentValue = DateTime.Now;
        }
    }
}
