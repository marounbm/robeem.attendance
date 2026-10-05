using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>The six KPIs of the integration health report (P13, study §17.3), computed from the existing tables.</summary>
public sealed class IntegrationHealthKpiTests : AttendanceEngineTestBase
{
    private readonly IntegrationResilience _resilience;
    private readonly int _integrationId;

    public IntegrationHealthKpiTests()
    {
        Clock.Local = Monday.At(12, 0);
        _resilience = new IntegrationResilience(Options.Create(new AttendanceSyncOptions()), Clock);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        _integrationId = db.AttendanceIntegrations.Single().Id;

        // The base fixture's seed row is not a delivery of this test's window.
        db.AttendanceProcessingBatches.Single().StartedAt = Monday.AddDays(-30);
        db.SaveChanges();
    }

    [Fact]
    public async Task Kpis_AreComputedOverTheWindow_ForThisIntegrationOnly()
    {
        var device = SeedDeliveries();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var report = await new IntegrationHealthService(db, _resilience, Options.Create(new AttendanceSyncOptions()), CreateAudit(db), Clock)
            .GetIntegrationHealthAsync(_integrationId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal("Front door", device.Name);
        Assert.Equal(Monday.At(12, 0).AddHours(-24), report.WindowStart);

        // Sync success rate: one successful batch, one failed.
        Assert.Equal((2, 1, 0.5), (report.Syncs, report.FailedSyncs, report.SyncSuccessRate));

        // Failed transaction rate: 1 of the 5 received in the window (4 polled and 1 uploaded; another is three days old).
        Assert.Equal((5, 1, 0.2), (report.TransactionsReceived, report.TransactionsFailed, report.FailedTransactionRate));

        // Duplicate rate: 1 of 3 normalized.
        Assert.Equal((3, 1), (report.TransactionsNormalized, report.Duplicates));
        Assert.Equal(1.0 / 3, report.DuplicateRate!.Value, 6);

        // Missing event rate: 1 missing punch over the 1 employee-day that had punches.
        Assert.Equal((1, 1, 1.0), (report.EmployeeDays, report.MissingPunchExceptions, report.MissingEventRate));

        // Average delay: (30 + 90 + 60) / 3 seconds; the failed row has no time, the uploaded file is left out.
        Assert.Equal(60.0, report.AverageSyncDelaySeconds);

        // Unresolved: the open exception of the delivered day; the closed one and the one of a day with no punches are not.
        Assert.Equal(1, report.UnresolvedExceptions);
    }

    [Fact]
    public async Task WithNothingInTheWindow_TheRatesAreNull_NotZero()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var report = await new IntegrationHealthService(db, _resilience, Options.Create(new AttendanceSyncOptions()), CreateAudit(db), Clock)
            .GetIntegrationHealthAsync(_integrationId, CancellationToken.None);

        Assert.NotNull(report);
        Assert.Null(report.SyncSuccessRate);
        Assert.Null(report.FailedTransactionRate);
        Assert.Null(report.DuplicateRate);
        Assert.Null(report.MissingEventRate);
        Assert.Null(report.AverageSyncDelaySeconds);
        Assert.Equal(0, report.UnresolvedExceptions);
    }

    private AttendanceDevice SeedDeliveries()
    {
        var now = new DateTimeOffset(Clock.Local, TimeSpan.Zero);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var device = db.AttendanceDevices.Single();

        var synced = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.IncrementalSync, StartedAt = Clock.Local.AddHours(-2), Status = AttendanceBatchStatus.Succeeded, RecordsIngested = 4 };
        var failed = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.IncrementalSync, StartedAt = Clock.Local.AddHours(-1), Status = AttendanceBatchStatus.Failed, ErrorSummary = "vendor is down" };
        var upload = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload, StartedAt = Clock.Local.AddHours(-1) };
        var olderSync = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.IncrementalSync, StartedAt = Clock.Local.AddDays(-3), Status = AttendanceBatchStatus.Succeeded };

        RawAttendanceTransaction Raw(AttendanceProcessingBatch batch, DateTimeOffset received, int? delaySeconds, RawTransactionProcessingStatus status, string hash) => new()
        {
            Integration = integration,
            ImportBatch = batch,
            RecordReceivedAt = received,
            TransactionTimestampParsed = delaySeconds is { } d ? received.AddSeconds(-d) : null,
            TransactionTimestampRaw = "-",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            ProcessingStatus = status,
            RecordHash = hash,
        };

        var r1 = Raw(synced, now.AddHours(-1), 30, RawTransactionProcessingStatus.Normalized, "h1");
        var r2 = Raw(synced, now.AddHours(-1), 90, RawTransactionProcessingStatus.Normalized, "h2");
        var r3 = Raw(synced, now.AddHours(-1), 60, RawTransactionProcessingStatus.Normalized, "h3");
        var r4 = Raw(synced, now.AddHours(-1), null, RawTransactionProcessingStatus.Failed, "h4");

        // Outside the window, and an uploaded one (a day-old file would dominate the delay).
        var old = Raw(olderSync, now.AddDays(-3), 10, RawTransactionProcessingStatus.Normalized, "h5");
        var uploaded = Raw(upload, now.AddHours(-1), 86400, RawTransactionProcessingStatus.Normalized, "h6");
        db.AddRange(failed, r1, r2, r3, r4, old, uploaded);
        db.SaveChanges();

        NormalizedAttendanceTransaction Normalized(RawAttendanceTransaction raw, NormalizedEventType type, int hour, DuplicateStatus duplicate) => new()
        {
            EmployeeId = EmployeeId,
            DeviceId = device.Id,
            IntegrationId = integration.Id,
            RawTransactionId = raw.Id,
            AttendanceDate = Monday,
            EventTimestampUtc = new DateTimeOffset(Monday.At(hour, 0), TimeSpan.Zero),
            EventTimestampLocal = Monday.At(hour, 0),
            TimeZoneId = "UTC",
            NormalizedEventType = type,
            DuplicateStatus = duplicate,
            DuplicateBucketTimestampUtc = new DateTimeOffset(Monday.At(hour, 0), TimeSpan.Zero),
        };

        db.NormalizedAttendanceTransactions.AddRange(
            Normalized(r1, NormalizedEventType.CheckIn, 8, DuplicateStatus.Unique),
            Normalized(r2, NormalizedEventType.CheckIn, 8, DuplicateStatus.DuplicateConfirmed),
            Normalized(r3, NormalizedEventType.CheckOut, 17, DuplicateStatus.Unique));

        db.AttendanceExceptions.AddRange(
            new AttendanceException { EmployeeId = EmployeeId, AttendanceDate = Monday, ExceptionType = AttendanceExceptionType.MissingPunch, MissingEventType = NormalizedEventType.BreakOut },
            new AttendanceException { EmployeeId = EmployeeId, AttendanceDate = Monday, ExceptionType = AttendanceExceptionType.InvalidSequence, Status = AttendanceExceptionStatus.Closed },
            new AttendanceException { EmployeeId = EmployeeId, AttendanceDate = Monday.AddDays(1), ExceptionType = AttendanceExceptionType.MissingPunch, MissingEventType = NormalizedEventType.CheckIn });
        db.SaveChanges();
        return device;
    }
}
