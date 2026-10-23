using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>
/// Worked scenarios through the whole pipeline: a CSV file in, ingestion to raw rows,
/// normalization, then the processing engine (P14). Appendix B Scenario 4, a duplicate from an
/// integration retry, is reconstructed here (see TEST_COVERAGE.md): the same file delivered twice
/// must not double the hours.
/// </summary>
public sealed class AttendanceEndToEndTests : AttendanceEngineTestBase
{
    // One shift's punches, as a device exports them. T1b is the same tap, 40 seconds later.
    private const string StandardDayCsv =
        "transaction_id,employee_id,timestamp,type,device_id\r\n" +
        "T1,E-1,2026-10-05 08:00:00,IN,DEV-1\r\n" +
        "T1b,E-1,2026-10-05 08:00:40,IN,DEV-1\r\n" +
        "T2,E-1,2026-10-05 12:00:00,BREAK OUT,DEV-1\r\n" +
        "T3,E-1,2026-10-05 13:00:00,BREAK IN,DEV-1\r\n" +
        "T4,E-1,2026-10-05 17:00:00,OUT,DEV-1\r\n";

    private readonly ServiceProvider _adapters = new ServiceCollection()
        .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
        .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
        .BuildServiceProvider();

    private readonly int _integrationId;

    public AttendanceEndToEndTests()
    {
        Clock.Local = Monday.At(18, 0);
        AssignSchedule(StandardDay);

        // The fixture's generic-csv provider gets the seeded Generic CSV format as its default, as at startup.
        Database.SeedAttendanceCatalog();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { DeviceId = db.AttendanceDevices.Single(d => d.IntegrationId == integration.Id).Id, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
        db.SaveChanges();
        _integrationId = integration.Id;
    }

    [Fact]
    public async Task Scenario1_AFileOfAStandardDay_ComesOutAsEightHours_WithTheDeviceDoubleTapAbsorbed()
    {
        await IngestAsync(StandardDayCsv);
        await NormalizeAllAsync();
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(5, db.RawAttendanceTransactions.Count(r => r.ExternalTransactionId != null));
        Assert.Equal(4, db.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.Unique));
        Assert.Equal(1, db.NormalizedAttendanceTransactions.Count(n => n.DuplicateStatus == DuplicateStatus.DuplicateConfirmed));

        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1), AttendanceRecordStatus.Approved), (record.WorkingHours, record.BreakDuration, record.Status));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task Scenario4_AnIntegrationRetryThatDeliversTheFileAgain_NeverContributesHours()
    {
        await IngestAsync(StandardDayCsv);

        // The retry: the same file again. Every row is stored raw (invariant 1) ...
        await IngestAsync(StandardDayCsv);
        await NormalizeAllAsync();
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(10, db.RawAttendanceTransactions.Count(r => r.ExternalTransactionId != null));
        Assert.All(db.RawAttendanceTransactions.Where(r => r.ExternalTransactionId != null), r => Assert.Equal(RawTransactionProcessingStatus.Normalized, r.ProcessingStatus));

        // ... but only the first delivery's four distinct punches are Unique (invariant 4).
        var normalized = db.NormalizedAttendanceTransactions.ToList();
        Assert.Equal(10, normalized.Count);
        var unique = normalized.Where(n => n.DuplicateStatus == DuplicateStatus.Unique).ToList();
        var duplicates = normalized.Where(n => n.DuplicateStatus == DuplicateStatus.DuplicateConfirmed).ToList();
        Assert.Equal(4, unique.Count);
        Assert.Equal(6, duplicates.Count);
        Assert.All(duplicates, d => Assert.NotNull(d.DuplicateOfTransactionId));

        // The hours are those of one day, not two, and no duplicate row is a source of the record.
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromHours(1)), (record.WorkingHours, record.BreakDuration));
        Assert.Equal(unique.Select(u => u.Id).OrderBy(id => id), record.SourceNormalizedTransactionIds!.Split(',').Select(int.Parse).OrderBy(id => id));
        Assert.Empty(duplicates.Select(d => d.Id).Intersect(record.SourceNormalizedTransactionIds!.Split(',').Select(int.Parse)));

        var summary = Assert.Single(db.AttendanceDaySummaries);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.Zero, 1), (summary.TotalWorkingHours, summary.TotalOvertimeHours, summary.ShiftCount));
        Assert.Empty(db.AttendanceExceptions);
    }

    [Fact]
    public async Task Scenario4_ReprocessingAfterTheRetry_ChangesNothing()
    {
        await IngestAsync(StandardDayCsv);
        await NormalizeAllAsync();
        await ProcessAsync(Monday);

        // The retry arrives after the day was already processed.
        await IngestAsync(StandardDayCsv);
        await NormalizeAllAsync();
        var again = await ProcessAsync(Monday);

        Assert.Equal(new DayProcessingResult(0, 1, 0), again);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((1, TimeSpan.FromHours(8)), (record.Version, record.WorkingHours));
    }

    [Fact]
    public async Task APendingDayProcessor_TurnsNormalizedPunchesIntoRecords_WithoutAManualReprocess_AndOnlyOnce()
    {
        await IngestAsync(StandardDayCsv);
        await NormalizeAllAsync();

        Assert.Equal(1, await ProcessPendingAsync());

        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var record = Assert.Single(db.AttendanceRecords);
            Assert.Equal((TimeSpan.FromHours(8), AttendanceRecordStatus.Approved), (record.WorkingHours, record.Status));
            Assert.All(db.NormalizedAttendanceTransactions, n => Assert.Equal(NormalizedTransactionProcessingStatus.Processed, n.ProcessingStatus));
        }

        // Nothing is pending any more, so the next run does no work.
        Assert.Equal(0, await ProcessPendingAsync());
    }

    private async Task<int> ProcessPendingAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        return await new PendingDayProcessor(db, CreateEngine(db), NullLogger<PendingDayProcessor>.Instance).ProcessPendingAsync(100, CancellationToken.None);
    }

    private async Task IngestAsync(string csv)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, csv);
            using var db = Database.CreateContext(TestDatabase.TenantA);
            var service = new IngestionService(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _adapters);
            Assert.NotNull(await service.IngestManualUploadAsync(_integrationId, path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task NormalizeAllAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var service = new NormalizationService(db, new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), _adapters, NullLogger<NormalizationService>.Instance);
        foreach (var batchId in await service.ListBatchesWithPendingTransactionsAsync(CancellationToken.None))
        {
            await service.NormalizeBatchAsync(batchId, CancellationToken.None);
        }
    }
}
