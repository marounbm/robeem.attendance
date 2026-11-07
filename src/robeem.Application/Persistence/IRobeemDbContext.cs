using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using robeem.DataModel;

namespace robeem.Application.Persistence;

/// <summary>
/// The current tenant's database, as application services see it. Implemented by
/// <c>robeem.Infrastructure.Persistence.RobeemDbContext</c>, which also enforces tenant isolation
/// (TenantId stamping, write guard, query filter).
/// </summary>
public interface IRobeemDbContext
{
    DbSet<Profile> Profiles { get; }

    DbSet<TimeSchedule> TimeSchedules { get; }

    DbSet<TimeScheduleShift> TimeScheduleShifts { get; }

    DbSet<TimeScheduleTeam> TimeScheduleTeams { get; }

    DbSet<TimeScheduleLine> TimeScheduleLines { get; }

    DbSet<TimeScheduleEmployee> TimeScheduleEmployees { get; }

    DbSet<AttendanceIntegration> AttendanceIntegrations { get; }

    DbSet<AttendanceConnection> AttendanceConnections { get; }

    DbSet<AttendanceDevice> AttendanceDevices { get; }

    DbSet<AttendancePolicy> AttendancePolicies { get; }

    DbSet<AttendanceProcessingBatch> AttendanceProcessingBatches { get; }

    DbSet<RawAttendanceTransaction> RawAttendanceTransactions { get; }

    DbSet<NormalizedAttendanceTransaction> NormalizedAttendanceTransactions { get; }

    DbSet<ExternalEmployeeIdMapping> ExternalEmployeeIdMappings { get; }

    DbSet<AttendanceRecord> AttendanceRecords { get; }

    DbSet<AttendanceDaySummary> AttendanceDaySummaries { get; }

    DbSet<AttendanceException> AttendanceExceptions { get; }

    DbSet<AttendanceCorrection> AttendanceCorrections { get; }

    DbSet<AttendanceApproval> AttendanceApprovals { get; }

    DbSet<PayPeriod> PayPeriods { get; }

    DbSet<AttendanceAuditLog> AttendanceAuditLogs { get; }

    DbSet<AttendanceUploadedFile> AttendanceUploadedFiles { get; }

    DbSet<AttendanceJobRun> AttendanceJobRuns { get; }

    ChangeTracker ChangeTracker { get; }

    /// <summary>Gets the database facade, for a transaction around several saves.</summary>
    DatabaseFacade Database { get; }

    /// <summary>Gets the current tenant id; throws when none is resolved, so tenant data access fails closed.</summary>
    string CurrentTenantId { get; }

    /// <summary>
    /// Lets the next save delete the never-processed raw rows of this batch and the uploaded-file record that points at it, which are otherwise
    /// immutable and insert-only. A row that was normalized, skipped or retained is still refused. Valid for one save, then forgotten.
    /// </summary>
    void AllowUnprocessedUploadDeletion(int batchId);

    void AddRange(params object[] entities);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
