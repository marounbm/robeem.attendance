using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Application.Security;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Deleting an upload that failed, or repeats an earlier one, with its punches: the single exception to the immutability of raw
/// attendance, allowed only while none of the punches was processed.
/// </summary>
public sealed class UploadDeletionTests : IDisposable
{
    private const string Header = "transaction_id,employee_id,timestamp,type,device_id\r\n";
    private const string GoodRows = "T1,E-1,2026-10-04 09:00:00,IN,DEV-1\r\nT2,E-1,2026-10-04 17:05,OUT,DEV-1\r\n";

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
        .BuildServiceProvider();

    private readonly List<string> _files = [];
    private readonly int _integrationId;

    public UploadDeletionTests()
    {
        _database.SeedAttendanceCatalog();
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = new AttendanceIntegration { ConnectorKey = "generic-csv", DisplayName = "HQ uploads" };
        db.AttendanceDevices.Add(new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "Asia/Beirut" });
        db.SaveChanges();
        _integrationId = integration.Id;
    }

    public void Dispose()
    {
        foreach (var file in _files)
        {
            File.Delete(file);
        }

        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task AFileThatCouldNotBeRead_IsOfferedForDeletion_AndGoesWithItsRecord_LeavingAnAuditEntry()
    {
        var unreadable = await UploadAsync("wrong.csv", "foo,bar\r\n1,2\r\n");
        var good = await UploadAsync("good.csv", Header + GoodRows);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var listed = (await CreateIngestion(db).ListUploadedFilesAsync(null, 1, 50, CancellationToken.None)).Items.ToDictionary(i => i.FileName);
            Assert.Equal("Failed", listed["wrong.csv"].Status);
            Assert.True(listed["wrong.csv"].CanDelete);
            Assert.False(listed["good.csv"].CanDelete);
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var result = await CreateDeletion(db).DeleteAsync(await UploadIdAsync(unreadable), CancellationToken.None);
            Assert.Equal(UploadDeletionOutcome.Deleted, result.Outcome);
        }

        using var read = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["good.csv"], read.AttendanceUploadedFiles.Select(f => f.FileName));
        Assert.Equal([good], read.AttendanceProcessingBatches.Select(b => b.Id));
        Assert.Equal(2, read.RawAttendanceTransactions.Count());

        var entry = Assert.Single(read.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.UploadDeleted));
        Assert.Contains("wrong.csv", entry.Details);
        Assert.Contains("failed upload", entry.Details);
    }

    [Fact]
    public async Task AFileWhoseEveryPunchWasUnusable_IsDeletedWithThoseUnprocessedPunches()
    {
        var batchId = await UploadAsync("junk.csv", Header + "T1,E-1,not a time,IN,DEV-1\r\nT2,E-1,nor this,OUT,DEV-1\r\n");

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.All(db.RawAttendanceTransactions.Where(r => r.ImportBatchId == batchId), r => Assert.Equal(RawTransactionProcessingStatus.Failed, r.ProcessingStatus));

        var result = await CreateDeletion(db).DeleteAsync(await UploadIdAsync(batchId), CancellationToken.None);

        Assert.Equal((UploadDeletionOutcome.Deleted, 2), (result.Outcome, result.PunchesDeleted));
        Assert.Empty(db.RawAttendanceTransactions);
        Assert.Empty(db.AttendanceUploadedFiles);
        Assert.Empty(db.AttendanceProcessingBatches);
    }

    [Fact]
    public async Task ARepeatOfAnEarlierFile_CanBeDeletedWhileItsPunchesAreUnprocessed_AndTheFirstCopyCannot()
    {
        var first = await UploadAsync("a.csv", Header + GoodRows);
        var repeat = await UploadAsync("a-copy.csv", Header + GoodRows);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var listed = (await CreateIngestion(db).ListUploadedFilesAsync(null, 1, 50, CancellationToken.None)).Items.ToDictionary(i => i.FileName);
            Assert.Equal((false, true), (listed["a.csv"].CanDelete, listed["a-copy.csv"].CanDelete));

            var refused = await CreateDeletion(db).DeleteAsync(await UploadIdAsync(first), CancellationToken.None);
            Assert.Equal(UploadDeletionOutcome.NotAllowed, refused.Outcome);
            Assert.Contains("not a repeat", refused.Reason);
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var result = await CreateDeletion(db).DeleteAsync(await UploadIdAsync(repeat), CancellationToken.None);
            Assert.Equal((UploadDeletionOutcome.Deleted, 2), (result.Outcome, result.PunchesDeleted));
        }

        // Only the repeat's punches went; the first copy still has its own.
        using var read = _database.CreateContext(TestDatabase.TenantA);
        Assert.Equal(["a.csv"], read.AttendanceUploadedFiles.Select(f => f.FileName));
        Assert.Equal(2, read.RawAttendanceTransactions.Count(r => r.ImportBatchId == first));
        Assert.Equal(2, read.RawAttendanceTransactions.Count());
    }

    [Theory]
    [InlineData(RawTransactionProcessingStatus.Normalized)]
    [InlineData(RawTransactionProcessingStatus.Skipped)]
    public async Task ARepeatWithAProcessedPunch_IsKept_AndNotOffered(RawTransactionProcessingStatus status)
    {
        await UploadAsync("a.csv", Header + GoodRows);
        var repeat = await UploadAsync("a-copy.csv", Header + GoodRows);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.RawAttendanceTransactions.First(r => r.ImportBatchId == repeat).ProcessingStatus = status;
            db.SaveChanges();
        }

        using var read = _database.CreateContext(TestDatabase.TenantA);
        var listed = (await CreateIngestion(read).ListUploadedFilesAsync(null, 1, 50, CancellationToken.None)).Items.ToDictionary(i => i.FileName);
        var result = await CreateDeletion(read).DeleteAsync(await UploadIdAsync(repeat), CancellationToken.None);

        Assert.False(listed["a-copy.csv"].CanDelete);
        Assert.Equal(UploadDeletionOutcome.NotAllowed, result.Outcome);
        Assert.Contains("already processed", result.Reason);
        Assert.Equal(4, read.RawAttendanceTransactions.Count());
        Assert.Equal(2, read.AttendanceUploadedFiles.Count());
        Assert.Empty(read.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.UploadDeleted));
    }

    [Fact]
    public async Task APunchNoLongerActive_KeepsItsRepeat()
    {
        await UploadAsync("a.csv", Header + GoodRows);
        var repeat = await UploadAsync("a-copy.csv", Header + GoodRows);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.RawAttendanceTransactions.First(r => r.ImportBatchId == repeat).RetentionStatus = RawTransactionRetentionStatus.Archived;
            db.SaveChanges();
        }

        using var read = _database.CreateContext(TestDatabase.TenantA);
        var result = await CreateDeletion(read).DeleteAsync(await UploadIdAsync(repeat), CancellationToken.None);

        Assert.Equal(UploadDeletionOutcome.NotAllowed, result.Outcome);
        Assert.Equal(4, read.RawAttendanceTransactions.Count());
    }

    [Fact]
    public async Task AnUnknownUpload_IsNotFound_AndOneOfAnotherTenantIsToo()
    {
        await UploadAsync("junk.csv", "foo,bar\r\n1,2\r\n");
        int uploadId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            uploadId = await db.AttendanceUploadedFiles.Select(f => f.Id).SingleAsync();
        }

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Equal(UploadDeletionOutcome.NotFound, (await CreateDeletion(other).DeleteAsync(uploadId, CancellationToken.None)).Outcome);
        Assert.Equal(UploadDeletionOutcome.NotFound, (await CreateDeletion(other).DeleteAsync(99999, CancellationToken.None)).Outcome);
    }

    private static UploadDeletionService CreateDeletion(RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new SystemCurrentUser(), TimeProvider.System));

    private IngestionService CreateIngestion(RobeemDbContext db) =>
        new(db, _database.CreateCatalogReader(), new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services);

    /// <summary>Uploads a file as a person would and returns the id of its batch.</summary>
    private async Task<int> UploadAsync(string name, string content)
    {
        var path = Path.GetTempFileName();
        _files.Add(path);
        await File.WriteAllTextAsync(path, content);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var result = await CreateIngestion(db).IngestUploadedFileAsync(_integrationId, path, name, null, CancellationToken.None);
        return result!.BatchId;
    }

    private async Task<int> UploadIdAsync(int batchId)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await db.AttendanceUploadedFiles.Where(f => f.BatchId == batchId).Select(f => f.Id).SingleAsync();
    }
}
