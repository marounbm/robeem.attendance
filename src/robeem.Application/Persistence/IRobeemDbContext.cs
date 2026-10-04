using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

    DbSet<AttendanceProvider> AttendanceProviders { get; }

    DbSet<AttendanceIntegration> AttendanceIntegrations { get; }

    DbSet<AttendanceDevice> AttendanceDevices { get; }

    DbSet<AttendancePolicy> AttendancePolicies { get; }

    DbSet<AttendanceProcessingBatch> AttendanceProcessingBatches { get; }

    DbSet<RawAttendanceTransaction> RawAttendanceTransactions { get; }

    DbSet<NormalizedAttendanceTransaction> NormalizedAttendanceTransactions { get; }

    DbSet<ExternalEmployeeIdMapping> ExternalEmployeeIdMappings { get; }

    DbSet<AttendanceRecord> AttendanceRecords { get; }

    DbSet<AttendanceDaySummary> AttendanceDaySummaries { get; }

    ChangeTracker ChangeTracker { get; }

    /// <summary>Gets the current tenant id; throws when none is resolved, so tenant data access fails closed.</summary>
    string CurrentTenantId { get; }

    void AddRange(params object[] entities);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
