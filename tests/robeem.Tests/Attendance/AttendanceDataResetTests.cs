using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// An administrator can delete all of a tenant's attendance data to import it again: the punches, the days calculated from them, and what was
/// raised or decided on them; never the configuration, the employees or the audit log, and never another tenant's data. It needs the host's
/// permission and the tenant id typed. The shift is 08:00-17:00 with a break from 12:00 to 13:00.
/// </summary>
public sealed class AttendanceDataResetTests : AttendanceEngineTestBase
{
    [Fact]
    public async Task ResetDeletesEveryKindOfAttendanceData_AndKeepsConfigurationEmployeesAndTheAuditLog()
    {
        await ImportADayAsync();
        AddOtherTenantsPunch();

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var before = await Service(db, enabled: true).GetSummaryAsync(CancellationToken.None);
            Assert.All(new[] { "Raw transactions", "Normalized transactions", "Attendance records", "Day summaries", "Exceptions", "Exception groups" }, name => Assert.True(Count(before.Deletes, name) > 0, name));
        }

        AttendanceDataResetResult result;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            result = await Service(db, enabled: true).ResetAsync(TestDatabase.TenantA, CancellationToken.None);
        }

        Assert.Equal(AttendanceDataResetOutcome.Done, result.Outcome);
        Assert.True(Count(result.Deleted, "Raw transactions") > 0);

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(check.RawAttendanceTransactions);
        Assert.Empty(check.NormalizedAttendanceTransactions);
        Assert.Empty(check.AttendanceProcessingBatches);
        Assert.Empty(check.AttendanceRecords);
        Assert.Empty(check.AttendanceDaySummaries);
        Assert.Empty(check.AttendanceExceptions);
        Assert.Empty(check.AttendanceExceptionGroups);
        Assert.Empty(check.AttendanceApprovals);
        Assert.Empty(check.AttendanceCorrections);

        // Kept: the configuration, the employee, and an audit entry for the deletion itself.
        Assert.Single(check.AttendanceIntegrations);
        Assert.Single(check.AttendanceDevices);
        Assert.Single(check.Profiles);
        var entry = Assert.Single(check.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.AttendanceDataReset);
        Assert.Contains("Configuration, employees and the audit log were kept", entry.Details);

        // Another tenant's punches are not touched.
        using var other = Database.CreateContext(TestDatabase.TenantB);
        Assert.Single(other.RawAttendanceTransactions);
    }

    [Fact]
    public async Task ResetNeedsTheHostToAllowIt_AndTheTenantIdTyped_OrNothingIsDeleted()
    {
        await ImportADayAsync();

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var off = await Service(db, enabled: false).ResetAsync(TestDatabase.TenantA, CancellationToken.None);
            Assert.Equal(AttendanceDataResetOutcome.Disabled, off.Outcome);
        }

        foreach (var typed in new[] { string.Empty, "tenant-b", "TENANT-A", "yes" })
        {
            using var db = Database.CreateContext(TestDatabase.TenantA);
            var wrong = await Service(db, enabled: true).ResetAsync(typed, CancellationToken.None);
            Assert.Equal(AttendanceDataResetOutcome.ConfirmationMismatch, wrong.Outcome);
        }

        using var check = Database.CreateContext(TestDatabase.TenantA);
        Assert.NotEmpty(check.NormalizedAttendanceTransactions);
        Assert.NotEmpty(check.AttendanceRecords);
        Assert.DoesNotContain(check.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.AttendanceDataReset);
    }

    [Fact]
    public async Task TheBulkDelete_RefusesToRunOutsideATransaction()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.DeleteAllAttendanceDataAsync(CancellationToken.None));
    }

    private static int Count(IEnumerable<robeem.Contracts.Attendance.AttendanceDataCount> counts, string name) => counts.Single(c => c.Name == name).Count;

    private AttendanceDataResetService Service(RobeemDbContext db, bool enabled) =>
        new(db, CreateAudit(db), Options.Create(new AttendanceDataResetOptions { Enabled = enabled }));

    /// <summary>A day calculated twice (so a record supersedes another), with an exception raised, a repeated punch pointing at its original, and an approval.</summary>
    private async Task ImportADayAsync()
    {
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(20, 0);
        AddPunch(NormalizedEventType.CheckIn, Monday.At(8, 0));
        await ProcessAsync(Monday);

        AddPunch(NormalizedEventType.CheckOut, Monday.At(17, 0));
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var first = db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).First();
        db.NormalizedAttendanceTransactions.Add(new NormalizedAttendanceTransaction
        {
            EmployeeId = EmployeeId,
            DeviceId = first.DeviceId,
            IntegrationId = first.IntegrationId,
            RawTransactionId = first.RawTransactionId,
            AttendanceDate = first.AttendanceDate,
            EventTimestampUtc = first.EventTimestampUtc,
            EventTimestampLocal = first.EventTimestampLocal,
            TimeZoneId = "UTC",
            NormalizedEventType = first.NormalizedEventType,
            ConfidenceStatus = EventConfidenceStatus.Certain,
            DuplicateStatus = DuplicateStatus.DuplicateConfirmed,
            DuplicateOfTransactionId = first.Id,
            DuplicateBucketTimestampUtc = first.EventTimestampUtc,
        });
        db.SaveChanges();
        Assert.Contains(db.AttendanceRecords, r => r.SupersededByRecordId != null);
    }

    private void AddOtherTenantsPunch()
    {
        using var db = Database.CreateContext(TestDatabase.TenantB);
        var integration = new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = "B" };
        db.RawAttendanceTransactions.Add(new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload },
            TransactionTimestampRaw = "-",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = "hash-b",
        });
        db.SaveChanges();
    }
}
