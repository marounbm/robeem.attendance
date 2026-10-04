using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using robeem.Application.Persistence;
using robeem.Application.Tenancy;
using robeem.DataModel;
using robeem.Infrastructure.Tenancy;

namespace robeem.Infrastructure.Persistence;

/// <summary>
/// The robeem database of ONE tenant. Each tenant has its own database (the connection string is
/// picked per scope from <see cref="ITenantContext"/>). On top of that physical separation, every
/// table carries a <c>TenantId</c> column, as defense in depth:
/// <list type="bullet">
/// <item>entities implementing <see cref="ITenantScoped"/> map it to their CLR property; all other
/// entities (e.g. the shared <c>TimeSchedule*</c> types) get it as a shadow property;</item>
/// <item>it is stamped on insert, and a write touching another tenant's row throws;</item>
/// <item>a global query filter returns only the current tenant's rows.</item>
/// </list>
/// </summary>
public class RobeemDbContext : DbContext, IRobeemDbContext
{
    public const string TenantIdProperty = "TenantId";

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcDateTimeOffsetConverter =
        new(v => v.ToUniversalTime(), v => v);

    private readonly ITenantContext _tenantContext;

    public RobeemDbContext(DbContextOptions<RobeemDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<TimeSchedule> TimeSchedules => Set<TimeSchedule>();

    public DbSet<TimeScheduleShift> TimeScheduleShifts => Set<TimeScheduleShift>();

    public DbSet<TimeScheduleTeam> TimeScheduleTeams => Set<TimeScheduleTeam>();

    public DbSet<TimeScheduleLine> TimeScheduleLines => Set<TimeScheduleLine>();

    public DbSet<TimeScheduleEmployee> TimeScheduleEmployees => Set<TimeScheduleEmployee>();

    public DbSet<AttendanceProvider> AttendanceProviders => Set<AttendanceProvider>();

    public DbSet<AttendanceIntegration> AttendanceIntegrations => Set<AttendanceIntegration>();

    public DbSet<AttendanceDevice> AttendanceDevices => Set<AttendanceDevice>();

    public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();

    public DbSet<AttendanceProcessingBatch> AttendanceProcessingBatches => Set<AttendanceProcessingBatch>();

    public DbSet<RawAttendanceTransaction> RawAttendanceTransactions => Set<RawAttendanceTransaction>();

    public DbSet<NormalizedAttendanceTransaction> NormalizedAttendanceTransactions => Set<NormalizedAttendanceTransaction>();

    public DbSet<ExternalEmployeeIdMapping> ExternalEmployeeIdMappings => Set<ExternalEmployeeIdMapping>();

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<AttendanceDaySummary> AttendanceDaySummaries => Set<AttendanceDaySummary>();

    public DbSet<AttendanceException> AttendanceExceptions => Set<AttendanceException>();

    /// <summary>Gets the current tenant id; throws when none is resolved, so tenant data access fails closed.</summary>
    public string CurrentTenantId => _tenantContext.TenantId ?? throw new TenantNotResolvedException();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantAndAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TenantId first, so entity configurations can include it in their indexes.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null && !t.IsOwned()).ToList())
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property<string>(TenantIdProperty).HasMaxLength(ITenantScoped.TenantIdMaxLength).IsRequired();
            entity.HasQueryFilter(BuildTenantFilter(entityType.ClrType));
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RobeemDbContext).Assembly);

        // Nothing in robeem is removed by cascade: HR and attendance history must be deleted
        // deliberately, row by row.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }

        // robeem stores local wall-clock times (the shared entities default to DateTime.Now).
        // Npgsql maps DateTime to `timestamp with time zone` by default, which only accepts UTC
        // values, so map every DateTime to `timestamp without time zone` instead. (Provider check
        // because the tests run on SQLite.)
        if (Database.IsNpgsql())
        {
            var dateTimeProperties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var property in dateTimeProperties)
            {
                property.SetColumnType("timestamp without time zone");
            }
        }

        // DateTimeOffset is for instants (punch times, receipt times) and is stored in UTC.
        // Npgsql maps it to `timestamp with time zone` and refuses a non-zero offset, so convert on
        // write. Applied on every provider so the SQLite tests store the same values.
        var dateTimeOffsetProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?));

        foreach (var property in dateTimeOffsetProperties)
        {
            property.SetValueConverter(UtcDateTimeOffsetConverter);
        }
    }

    private static void EnsureRawTransactionImmutable(EntityEntry entry)
    {
        if (entry.Entity is not RawAttendanceTransaction)
        {
            return;
        }

        if (entry.State == EntityState.Deleted)
        {
            throw new InvalidOperationException("RawAttendanceTransaction is immutable.");
        }

        // Normalization and retention may move the bookkeeping columns; nothing else may change.
        var changed = entry.Properties
            .Where(p => p.IsModified && !RawAttendanceTransaction.MutableProperties.Contains(p.Metadata.Name))
            .Select(p => p.Metadata.Name)
            .ToList();
        if (changed.Count > 0)
        {
            throw new InvalidOperationException(
                $"RawAttendanceTransaction is immutable. Only {string.Join(", ", RawAttendanceTransaction.MutableProperties)} may change; attempted: {string.Join(", ", changed)}.");
        }
    }

    private static void ApplyAudit(EntityEntry entry)
    {
        if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(nameof(Entity.ModifiedOn)) is not null)
        {
            entry.Property(nameof(Entity.ModifiedOn)).CurrentValue = DateTime.Now;
        }
    }

    private LambdaExpression BuildTenantFilter(Type clrType)
    {
        // e => EF.Property<string>(e, "TenantId") == this.CurrentTenantId
        // EF Core re-binds `this` to the executing context instance and sends the value as a parameter.
        var entity = Expression.Parameter(clrType, "e");
        var rowTenant = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            new[] { typeof(string) },
            entity,
            Expression.Constant(TenantIdProperty));
        var currentTenant = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));

        return Expression.Lambda(Expression.Equal(rowTenant, currentTenant), entity);
    }

    private void ApplyTenantAndAuditRules()
    {
        var tenantId = CurrentTenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            var tenantProperty = entry.Property(TenantIdProperty);

            switch (entry.State)
            {
                case EntityState.Added:
                    var assigned = tenantProperty.CurrentValue as string;
                    if (string.IsNullOrEmpty(assigned))
                    {
                        tenantProperty.CurrentValue = tenantId;
                    }
                    else if (!string.Equals(assigned, tenantId, StringComparison.Ordinal))
                    {
                        throw new TenantIsolationException(
                            $"Cannot insert {entry.Metadata.ClrType.Name} for tenant '{assigned}' from tenant '{tenantId}'.");
                    }

                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    if (!string.Equals(tenantProperty.OriginalValue as string, tenantId, StringComparison.Ordinal)
                        || !string.Equals(tenantProperty.CurrentValue as string, tenantId, StringComparison.Ordinal))
                    {
                        throw new TenantIsolationException(
                            $"Cannot change {entry.Metadata.ClrType.Name} of tenant '{tenantProperty.OriginalValue}' from tenant '{tenantId}'.");
                    }

                    EnsureRawTransactionImmutable(entry);
                    break;
            }

            ApplyAudit(entry);
        }
    }
}
