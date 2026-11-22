using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using robeem.Contracts.Attendance;
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

    DbSet<PublicHoliday> PublicHolidays { get; }

    DbSet<EmployeeLeave> EmployeeLeaves { get; }

    DbSet<TimeSchedule> TimeSchedules { get; }

    DbSet<OnDemandTimeSchedule> OnDemandTimeSchedules { get; }

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

    DbSet<AttendanceExceptionGroup> AttendanceExceptionGroups { get; }

    DbSet<AttendanceCorrection> AttendanceCorrections { get; }

    DbSet<AttendanceApproval> AttendanceApprovals { get; }

    DbSet<PayPeriod> PayPeriods { get; }

    DbSet<AttendanceSettings> AttendanceSettings { get; }

    DbSet<AttendanceRuleSet> AttendanceRuleSets { get; }

    DbSet<AttendanceRule> AttendanceRules { get; }

    DbSet<AttendanceRuleCondition> AttendanceRuleConditions { get; }

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

    /// <summary>
    /// Deletes every row of the current tenant's attendance data (raw and normalized punches, batches, uploads, calculated days, exceptions,
    /// corrections, approvals, job runs) and returns how many of each. It keeps the configuration, the employees and the audit log. This is the one
    /// bulk path that deletes raw attendance, and only <c>AttendanceDataResetService</c> uses it: the caller must have a transaction open (so it is
    /// all or nothing), checks that the host allows it, and audits it. It throws when there is no transaction.
    /// </summary>
    Task<IReadOnlyList<AttendanceDataCount>> DeleteAllAttendanceDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the given employees and everything of theirs: their corrections, exceptions, approvals, calculated days, normalized punches, the raw punches
    /// those came from (and any sent under their id on their devices), the uploaded files and batches that held nothing else, their employee-id mappings,
    /// schedule assignments and leaves, and the employees themselves. Returns how many of each (with <paramref name="dryRun"/>, how many there would be, and
    /// nothing is deleted). It keeps the audit log. This is the second bulk path that deletes raw attendance, and only <c>TestEmployeeRemover</c> uses it: the
    /// caller must have a transaction open for a real delete (so it is all or nothing), checks that the host allows it, and audits it.
    /// </summary>
    Task<IReadOnlyList<AttendanceDataCount>> DeleteEmployeesAndTheirDataAsync(IReadOnlyCollection<int> employeeIds, bool dryRun, CancellationToken cancellationToken);

    void AddRange(params object[] entities);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
