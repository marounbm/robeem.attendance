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

    private readonly ITenantContext _tenantContext;

    // The batches whose never-processed upload may be deleted by the next save (see AllowUnprocessedUploadDeletion).
    private readonly HashSet<int> _uploadDeletionBatchIds = [];

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

    public DbSet<AttendanceIntegration> AttendanceIntegrations => Set<AttendanceIntegration>();

    public DbSet<AttendanceConnection> AttendanceConnections => Set<AttendanceConnection>();

    public DbSet<AttendanceDevice> AttendanceDevices => Set<AttendanceDevice>();

    public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();

    public DbSet<AttendanceProcessingBatch> AttendanceProcessingBatches => Set<AttendanceProcessingBatch>();

    public DbSet<RawAttendanceTransaction> RawAttendanceTransactions => Set<RawAttendanceTransaction>();

    public DbSet<NormalizedAttendanceTransaction> NormalizedAttendanceTransactions => Set<NormalizedAttendanceTransaction>();

    public DbSet<ExternalEmployeeIdMapping> ExternalEmployeeIdMappings => Set<ExternalEmployeeIdMapping>();

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<AttendanceDaySummary> AttendanceDaySummaries => Set<AttendanceDaySummary>();

    public DbSet<AttendanceException> AttendanceExceptions => Set<AttendanceException>();

    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

    public DbSet<AttendanceApproval> AttendanceApprovals => Set<AttendanceApproval>();

    public DbSet<PayPeriod> PayPeriods => Set<PayPeriod>();

    public DbSet<AttendanceAuditLog> AttendanceAuditLogs => Set<AttendanceAuditLog>();

    public DbSet<AttendanceUploadedFile> AttendanceUploadedFiles => Set<AttendanceUploadedFile>();

    public DbSet<AttendanceJobRun> AttendanceJobRuns => Set<AttendanceJobRun>();

    /// <summary>Gets the current tenant id; throws when none is resolved, so tenant data access fails closed.</summary>
    public string CurrentTenantId => _tenantContext.TenantId ?? throw new TenantNotResolvedException();

    public void AllowUnprocessedUploadDeletion(int batchId) => _uploadDeletionBatchIds.Add(batchId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            ApplyTenantAndAuditRules();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        finally
        {
            _uploadDeletionBatchIds.Clear();
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            ApplyTenantAndAuditRules();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        finally
        {
            // The permission is for one save, whether or not it worked.
            _uploadDeletionBatchIds.Clear();
        }
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

        // The catalog's own configurations belong to CatalogDbContext; loading them here would drag the catalog tables in.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RobeemDbContext).Assembly, type => !PersistenceConventions.IsCatalogType(type));

        PersistenceConventions.Apply(modelBuilder, Database.IsNpgsql());
    }

    private static void ApplyAudit(EntityEntry entry)
    {
        if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(nameof(Entity.ModifiedOn)) is not null)
        {
            entry.Property(nameof(Entity.ModifiedOn)).CurrentValue = DateTime.Now;
        }
    }

    private void EnsureRawTransactionImmutable(EntityEntry entry)
    {
        if (entry.Entity is not RawAttendanceTransaction raw)
        {
            return;
        }

        if (entry.State == EntityState.Deleted)
        {
            // The one exception (CLAUDE.md §1.2): a row that was never processed, deleted with the failed or repeated upload it came from, which
            // the caller named for this save. A row that was normalized, skipped or retained is never deleted, whatever the caller says.
            var neverProcessed = entry.Property(nameof(RawAttendanceTransaction.ProcessingStatus)).OriginalValue is RawTransactionProcessingStatus.Pending or RawTransactionProcessingStatus.Failed;
            if (neverProcessed && _uploadDeletionBatchIds.Contains(raw.ImportBatchId))
            {
                return;
            }

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

    private void EnsureAuditLogAppendOnly(EntityEntry entry)
    {
        // The record of an upload goes with its never-processed rows, in the same save (see EnsureRawTransactionImmutable); it is never edited.
        if (entry is { State: EntityState.Deleted, Entity: AttendanceUploadedFile upload } && _uploadDeletionBatchIds.Contains(upload.BatchId))
        {
            return;
        }

        if (entry.Entity is AttendanceAuditLog or AttendanceUploadedFile)
        {
            throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only: a row is never changed or deleted.");
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
                    EnsureAuditLogAppendOnly(entry);
                    break;
            }

            ApplyAudit(entry);
        }
    }
}
