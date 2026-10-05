using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Application.Attendance;

public sealed class IntegrationHealthService : IIntegrationHealthService
{
    private readonly IRobeemDbContext _db;
    private readonly IntegrationResilience _resilience;
    private readonly AttendanceSyncOptions _options;
    private readonly IAttendanceAuditTrail _audit;
    private readonly TimeProvider _clock;

    public IntegrationHealthService(IRobeemDbContext db, IntegrationResilience resilience, IOptions<AttendanceSyncOptions> options, IAttendanceAuditTrail audit, TimeProvider clock)
    {
        _db = db;
        _resilience = resilience;
        _options = options.Value;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IntegrationHealthReport?> GetIntegrationHealthAsync(int integrationId, CancellationToken cancellationToken)
    {
        var integration = await _db.AttendanceIntegrations
            .AsNoTracking()
            .Include(i => i.Provider)
            .SingleOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null)
        {
            return null;
        }

        // The KPIs read raw and normalized attendance directly, outside the engine: audited (CLAUDE.md §2.8).
        _audit.Record(
            AttendanceAuditActions.AttendanceDataRead,
            $"{nameof(RawAttendanceTransaction)},{nameof(NormalizedAttendanceTransaction)}",
            null,
            $"Health KPIs of integration {integrationId}.");
        await _db.SaveChangesAsync(cancellationToken);

        var windowEnd = _clock.GetLocalNow().DateTime;
        var windowStart = windowEnd - TimeSpan.FromHours(Math.Max(1, _options.HealthWindowHours));
        var firstDate = windowStart.Date;

        // Every successful synchronization leaves a batch, a quiet one included (pruned after
        // EmptySyncRetentionDays), so the rate counts them all. A failed one is a provider failure:
        // no rows, and a reason.
        var syncs = _db.AttendanceProcessingBatches
            .Where(b => b.IntegrationId == integrationId && b.BatchType == AttendanceBatchType.IncrementalSync && b.StartedAt >= windowStart);
        var syncCount = await syncs.CountAsync(cancellationToken);
        var failedSyncs = await syncs.CountAsync(b => b.Status == AttendanceBatchStatus.Failed && b.ErrorSummary != null && b.RecordsIngested == 0, cancellationToken);

        var lastFailure = await _db.AttendanceProcessingBatches
            .AsNoTracking()
            .Where(b => b.IntegrationId == integrationId && b.Status == AttendanceBatchStatus.Failed && b.ErrorSummary != null && b.RecordsIngested == 0)
            .OrderByDescending(b => b.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // A transaction is in the window when its batch started in it: the same moment as its
        // receipt, in a column every database can compare (a DateTimeOffset it can't, on SQLite).
        var raw = _db.RawAttendanceTransactions.Where(r => r.IntegrationId == integrationId && r.ImportBatch!.StartedAt >= windowStart);
        var received = await raw.CountAsync(cancellationToken);
        var rawFailed = await raw.CountAsync(r => r.ProcessingStatus == RawTransactionProcessingStatus.Failed, cancellationToken);

        // Delay of a pushed or polled punch; an uploaded file is history by design and would swamp it.
        var timings = await raw
            .Where(r => r.TransactionTimestampParsed != null && r.ImportBatch!.BatchType != AttendanceBatchType.ManualUpload)
            .Select(r => new { r.TransactionTimestampParsed, r.RecordReceivedAt })
            .ToListAsync(cancellationToken);

        var normalized = _db.NormalizedAttendanceTransactions.Where(n => n.IntegrationId == integrationId && n.RawTransaction!.ImportBatch!.StartedAt >= windowStart);
        var normalizedCount = await normalized.CountAsync(cancellationToken);
        var duplicates = await normalized.CountAsync(n => n.DuplicateStatus == DuplicateStatus.DuplicateConfirmed, cancellationToken);

        // Exceptions belong to an employee and a date, not to an integration: they count for the
        // integrations that delivered a punch of that employee on that date.
        var employeeDays = await _db.NormalizedAttendanceTransactions
            .Where(n => n.IntegrationId == integrationId && n.EmployeeId != null && n.DuplicateStatus == DuplicateStatus.Unique && n.AttendanceDate >= firstDate)
            .Select(n => new { n.EmployeeId, n.AttendanceDate })
            .Distinct()
            .CountAsync(cancellationToken);

        var delivered = _db.AttendanceExceptions.Where(e => _db.NormalizedAttendanceTransactions.Any(n =>
            n.IntegrationId == integrationId && n.EmployeeId == e.EmployeeId && n.AttendanceDate == e.AttendanceDate));
        var missing = await delivered.CountAsync(e => e.ExceptionType == AttendanceExceptionType.MissingPunch && e.AttendanceDate >= firstDate, cancellationToken);
        var unresolved = await delivered.CountAsync(e => e.Status != AttendanceExceptionStatus.Closed, cancellationToken);

        return new IntegrationHealthReport
        {
            IntegrationId = integration.Id,
            DisplayName = integration.DisplayName,
            ProviderKey = integration.Provider!.ProviderKey,
            Status = integration.Status.ToString(),
            HealthStatus = integration.HealthStatus.ToString(),
            IsCircuitOpen = _resilience.Get(_db.CurrentTenantId, integration.Id).IsOpen,
            LastSuccessfulSyncAt = integration.LastSuccessfulSyncAt,
            LastFailureAt = lastFailure?.CompletedAt ?? lastFailure?.StartedAt,
            LastFailureMessage = lastFailure?.ErrorSummary,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Syncs = syncCount,
            FailedSyncs = failedSyncs,
            SyncSuccessRate = syncCount == 0 ? null : (double)(syncCount - failedSyncs) / syncCount,
            TransactionsReceived = received,
            TransactionsFailed = rawFailed,
            FailedTransactionRate = Rate(rawFailed, received),
            TransactionsNormalized = normalizedCount,
            Duplicates = duplicates,
            DuplicateRate = Rate(duplicates, normalizedCount),
            EmployeeDays = employeeDays,
            MissingPunchExceptions = missing,
            MissingEventRate = Rate(missing, employeeDays),
            AverageSyncDelaySeconds = timings.Count == 0
                ? null
                : timings.Average(t => Math.Max(0, (t.RecordReceivedAt - t.TransactionTimestampParsed!.Value).TotalSeconds)),
            UnresolvedExceptions = unresolved,
        };
    }

    private static double? Rate(int part, int whole) => whole == 0 ? null : (double)part / whole;
}
