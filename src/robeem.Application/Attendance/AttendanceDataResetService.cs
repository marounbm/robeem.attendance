using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using robeem.Application.Persistence;
using robeem.Contracts.Attendance;

namespace robeem.Application.Attendance;

public sealed class AttendanceDataResetService : IAttendanceDataResetService
{
    private readonly IRobeemDbContext _db;
    private readonly IAttendanceAuditTrail _audit;
    private readonly IOptions<AttendanceDataResetOptions> _options;

    public AttendanceDataResetService(IRobeemDbContext db, IAttendanceAuditTrail audit, IOptions<AttendanceDataResetOptions> options)
    {
        _db = db;
        _audit = audit;
        _options = options;
    }

    public async Task<AttendanceDataSummary> GetSummaryAsync(CancellationToken cancellationToken) => new()
    {
        TenantId = _db.CurrentTenantId,
        Enabled = _options.Value.Enabled,
        Deletes =
        [
            Count("Raw transactions", await _db.RawAttendanceTransactions.CountAsync(cancellationToken)),
            Count("Normalized transactions", await _db.NormalizedAttendanceTransactions.CountAsync(cancellationToken)),
            Count("Processing batches", await _db.AttendanceProcessingBatches.CountAsync(cancellationToken)),
            Count("Uploaded files", await _db.AttendanceUploadedFiles.CountAsync(cancellationToken)),
            Count("Attendance records", await _db.AttendanceRecords.CountAsync(cancellationToken)),
            Count("Day summaries", await _db.AttendanceDaySummaries.CountAsync(cancellationToken)),
            Count("Exceptions", await _db.AttendanceExceptions.CountAsync(cancellationToken)),
            Count("Exception groups", await _db.AttendanceExceptionGroups.CountAsync(cancellationToken)),
            Count("Corrections", await _db.AttendanceCorrections.CountAsync(cancellationToken)),
            Count("Approvals", await _db.AttendanceApprovals.CountAsync(cancellationToken)),
            Count("Job runs", await _db.AttendanceJobRuns.CountAsync(cancellationToken)),
        ],
        Keeps =
        [
            Count("Integrations", await _db.AttendanceIntegrations.CountAsync(cancellationToken)),
            Count("Connections", await _db.AttendanceConnections.CountAsync(cancellationToken)),
            Count("Devices", await _db.AttendanceDevices.CountAsync(cancellationToken)),
            Count("Employee-id mappings", await _db.ExternalEmployeeIdMappings.CountAsync(cancellationToken)),
            Count("Policies", await _db.AttendancePolicies.CountAsync(cancellationToken)),
            Count("Rule sets", await _db.AttendanceRuleSets.CountAsync(cancellationToken)),
            Count("Pay periods", await _db.PayPeriods.CountAsync(cancellationToken)),
            Count("Employees", await _db.Profiles.CountAsync(cancellationToken)),
            Count("Audit log entries", await _db.AttendanceAuditLogs.CountAsync(cancellationToken)),
        ],
    };

    public async Task<AttendanceDataResetResult> ResetAsync(string confirmation, CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            return AttendanceDataResetResult.Refused(AttendanceDataResetOutcome.Disabled);
        }

        // Typed exactly, so that a click, or a request sent to the wrong tenant, can't delete anything.
        var tenantId = _db.CurrentTenantId;
        if (!string.Equals(confirmation?.Trim(), tenantId, StringComparison.Ordinal))
        {
            return AttendanceDataResetResult.Refused(AttendanceDataResetOutcome.ConfirmationMismatch);
        }

        // The deletion and its audit entry are one transaction: the data is not deleted without a record that it was, and no record is made of a deletion that failed.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var deleted = await _db.DeleteAllAttendanceDataAsync(cancellationToken);
        _audit.Record(
            AttendanceAuditActions.AttendanceDataReset,
            "Tenant",
            null,
            $"Deleted all attendance data of tenant '{tenantId}': " + string.Join(", ", deleted.Where(d => d.Count > 0).Select(d => $"{d.Count} {d.Name.ToLowerInvariant()}")) + ". Configuration, employees and the audit log were kept.");
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AttendanceDataResetResult(AttendanceDataResetOutcome.Done, deleted);
    }

    private static AttendanceDataCount Count(string name, int count) => new() { Name = name, Count = count };
}
