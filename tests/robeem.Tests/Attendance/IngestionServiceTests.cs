using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

public sealed class IngestionServiceTests : IDisposable
{
    private const string SampleCsv =
        "transaction_id,employee_id,timestamp,type,device_id,fingerprint_template\r\n" +
        "T1,E-1,2026-10-04 09:00:00,IN,DEV-1,AAEC\r\n" +
        "T2,E-1,2026-10-04 17:05,OUT,DEV-1,\r\n" +
        "T3,E-2,2026-10-04T08:58:00+03:00,IN,DEV-9,\r\n" +
        "T4,E-2,04/10/2026 nine-ish,OUT,DEV-1,\r\n" +
        "\"T5\",\"E-3\",\"2026-10-04 12:00:00Z\",\"BREAK, OUT\",DEV-1,\r\n";

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _adapters = new ServiceCollection()
        .AddKeyedSingleton<IAttendanceProvider, GenericCsvProvider>(GenericCsvProvider.ProviderKey)
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _deviceId;

    public IngestionServiceTests()
    {
        File.WriteAllText(_csvPath, SampleCsv);

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = new AttendanceIntegration
        {
            Provider = new AttendanceProvider { ProviderKey = GenericCsvProvider.ProviderKey, DisplayName = "Generic CSV" },
            DisplayName = "HQ uploads",
        };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "Asia/Beirut" };
        db.AttendanceDevices.Add(device);
        db.SaveChanges();
        _integrationId = integration.Id;
        _deviceId = device.Id;
    }

    public void Dispose()
    {
        File.Delete(_csvPath);
        _adapters.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task ManualUpload_StoresEveryRow_AndCountsTheMalformedOneAsFailed()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var result = await CreateService(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(nameof(AttendanceBatchStatus.PartiallyFailed), result.Status);
        Assert.Equal(5, result.RecordsIngested);
        Assert.Equal(1, result.RecordsFailed);

        var batch = db.AttendanceProcessingBatches.Single(b => b.Id == result.BatchId);
        Assert.Equal(AttendanceBatchType.ManualUpload, batch.BatchType);
        Assert.Equal((5, 1), (batch.RecordsIngested, batch.RecordsFailed));
        Assert.NotNull(batch.CompletedAt);

        // Invariant 1: every row is stored, the malformed one included.
        var rows = db.RawAttendanceTransactions.OrderBy(r => r.Id).ToList();
        Assert.Equal(["T1", "T2", "T3", "T4", "T5"], rows.Select(r => r.ExternalTransactionId));
        Assert.All(rows, r => Assert.Equal(result.BatchId, r.ImportBatchId));
        Assert.All(rows, r => Assert.Equal(64, r.RecordHash.Length));

        var malformed = rows[3];
        Assert.Equal(RawTransactionProcessingStatus.Failed, malformed.ProcessingStatus);
        Assert.Equal("04/10/2026 nine-ish", malformed.TransactionTimestampRaw);
        Assert.Null(malformed.TransactionTimestampParsed);
        Assert.Contains("Unparseable timestamp", malformed.ProcessingError);
        Assert.All(rows.Where(r => r != malformed), r => Assert.Equal(RawTransactionProcessingStatus.Pending, r.ProcessingStatus));

        // Invariant 10: the template column never reaches the database.
        Assert.All(rows, r => Assert.DoesNotContain("fingerprint", r.RawPayload, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"employee_id\":\"E-1\"", rows[0].RawPayload);
    }

    [Fact]
    public async Task ManualUpload_ResolvesKnownDevices_AndStoresInstantsInUtc()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await CreateService(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        var rows = db.RawAttendanceTransactions.ToDictionary(r => r.ExternalTransactionId!);

        // Wall-clock time at a known device: its time zone applies (Beirut is UTC+3 in October).
        Assert.Equal(_deviceId, rows["T1"].DeviceId);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero), rows["T1"].TransactionTimestampParsed);

        // An explicit offset needs no device; the unknown device id is kept as sent.
        Assert.Null(rows["T3"].DeviceId);
        Assert.Equal("DEV-9", rows["T3"].DeviceExternalId);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 5, 58, 0, TimeSpan.Zero), rows["T3"].TransactionTimestampParsed);

        // Quoted fields keep their commas; nothing is interpreted.
        Assert.Equal("BREAK, OUT", rows["T5"].TransactionTypeRaw);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero), rows["T5"].TransactionTimestampParsed);
    }

    [Fact]
    public async Task ManualUpload_AttributesEveryRowToTheIntegrationsDeviceForUploads_AndReadsTimesInItsZone()
    {
        var uploadDeviceId = SetDeviceForUploads("Europe/London", AttendanceTransport.FileUpload);
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await CreateService(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        var rows = db.RawAttendanceTransactions.ToDictionary(r => r.ExternalTransactionId!);

        // The file's device id stays as sent; the punch is attributed to the integration's device, even for a registered one.
        Assert.All(rows.Values, r => Assert.Equal(uploadDeviceId, r.DeviceId));
        Assert.Equal("DEV-1", rows["T1"].DeviceExternalId);
        Assert.Equal("DEV-9", rows["T3"].DeviceExternalId);

        // 09:00 in London is 08:00 UTC in October (BST), not the 06:00 of the file's own device in Beirut.
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 8, 0, 0, TimeSpan.Zero), rows["T1"].TransactionTimestampParsed);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 5, 58, 0, TimeSpan.Zero), rows["T3"].TransactionTimestampParsed);
    }

    [Fact]
    public async Task TheDeviceForUploads_IsIgnored_ByAnIntegrationThatDoesNotReceiveFiles()
    {
        SetDeviceForUploads("Europe/London", AttendanceTransport.RestApi);
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await CreateService(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        var rows = db.RawAttendanceTransactions.ToDictionary(r => r.ExternalTransactionId!);
        Assert.Equal(_deviceId, rows["T1"].DeviceId);
        Assert.Null(rows["T3"].DeviceId);
    }

    [Fact]
    public async Task ManualUpload_WhenTheAdapterFails_MarksTheBatchFailedWithoutThrowing()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var result = await CreateService(db).IngestManualUploadAsync(_integrationId, _csvPath + ".missing", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(nameof(AttendanceBatchStatus.Failed), result.Status);
        Assert.Equal((0, 0), (result.RecordsIngested, result.RecordsFailed));
        Assert.False(string.IsNullOrEmpty(result.ErrorSummary));
        Assert.Empty(db.RawAttendanceTransactions);
    }

    [Fact]
    public async Task ManualUpload_ForAnUnknownIntegration_ReturnsNull()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        Assert.Null(await CreateService(db).IngestManualUploadAsync(_integrationId + 100, _csvPath, CancellationToken.None));
        Assert.Empty(db.AttendanceProcessingBatches);
    }

    [Fact]
    public async Task ManualUpload_ReadsAnExcelFile_WithTheGenericProvider_Too()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var xls = Path.Combine(AppContext.BaseDirectory, "TestData", "ZktecoExport.xls");

        var result = await CreateService(db).IngestManualUploadAsync(_integrationId, xls, CancellationToken.None);

        // The file is read: every row is stored. But the ZKTeco export is not in the generic layout (its timestamp
        // is "01-Apr-26 08:33:25", its employee column "No."), so every row is unusable: that export needs a ZKTeco integration.
        Assert.NotNull(result);
        Assert.Equal((369, 369), (result.RecordsIngested, result.RecordsFailed));
        Assert.Contains("Date/Time", db.RawAttendanceTransactions.First().RawPayload);

        // ... and the result says why, with an example, instead of only counting.
        Assert.Contains("Unparseable timestamp '01-Apr-26 08:33:25'", result.ErrorSummary);
    }

    [Fact]
    public async Task UploadTargets_AreTheActiveIntegrationsOfActiveFileProviders_WithTheirFormatsAndLayout()
    {
        using (var seed = _database.CreateContext(TestDatabase.TenantA))
        {
            var csv = seed.AttendanceProviders.Single(p => p.ProviderKey == GenericCsvProvider.ProviderKey);
            csv.SupportedTransports = "FileUpload,Webhook";
            var rest = new AttendanceProvider { ProviderKey = "rest-only", DisplayName = "REST", SupportedTransports = nameof(AttendanceTransport.RestApi) };
            var retired = new AttendanceProvider { ProviderKey = "retired", DisplayName = "Retired", SupportedTransports = nameof(AttendanceTransport.FileUpload), IsActive = false };
            seed.AttendanceIntegrations.AddRange(
                new AttendanceIntegration { Provider = csv, DisplayName = "B takes uploads" },
                new AttendanceIntegration { Provider = csv, DisplayName = "A takes uploads" },
                new AttendanceIntegration { Provider = csv, DisplayName = "Paused", Status = AttendanceIntegrationStatus.Paused },
                new AttendanceIntegration { Provider = csv, DisplayName = "Pushed by webhook", Transport = AttendanceTransport.Webhook },
                new AttendanceIntegration { Provider = rest, DisplayName = "Pulls only", Transport = AttendanceTransport.RestApi },
                new AttendanceIntegration { Provider = retired, DisplayName = "Retired provider" });
            seed.SaveChanges();
        }

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var targets = await CreateService(db).ListUploadTargetsAsync(CancellationToken.None);

        // The constructor's "HQ uploads" shares the provider, so it counts; paused, webhook, pull-only and retired ones do not.
        Assert.Equal(["A takes uploads", "B takes uploads", "HQ uploads"], targets.Select(t => t.DisplayName));
        Assert.All(targets, t => Assert.Equal([".csv", ".xls", ".xlsx"], t.FileFormats));
        Assert.All(targets, t => Assert.Equal("Generic CSV", t.ProviderName));
        Assert.Contains(targets[0].Layout.Fields, f => f.Target == "ExternalEmployeeId");
    }

    [Fact]
    public async Task AnUploadedFile_IsRecorded_WithItsNameSizeSha256AndUploader()
    {
        int uploaderId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var uploader = new Profile { EmployeeNumber = "HR-1", FirstName = "Hana", LastName = "Reed", Title = "Hana Reed" };
            db.Profiles.Add(uploader);
            db.SaveChanges();
            uploaderId = uploader.Id;
        }

        AttendanceIngestionResult? result;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            result = await CreateService(db).IngestUploadedFileAsync(_integrationId, _csvPath, @"C:\exports\April.csv", uploaderId, CancellationToken.None);
        }

        var expectedHash = Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(_csvPath)));
        Assert.Equal(expectedHash, result!.Sha256);
        Assert.Null(result.SameFileUploadedAt);

        using var read = _database.CreateContext(TestDatabase.TenantA);
        var file = Assert.Single(read.AttendanceUploadedFiles);
        Assert.Equal(("April.csv", expectedHash, new FileInfo(_csvPath).Length), (file.FileName, file.Sha256, file.FileSizeBytes));
        Assert.Equal((_integrationId, result.BatchId, uploaderId), (file.IntegrationId, file.BatchId, file.UploadedById));
        Assert.InRange(file.UploadedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task TheSameFileUploadedAgain_IsStoredAndFlaggedAsARepeat_ButAnotherFileIsNot()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);
        var first = await service.IngestUploadedFileAsync(_integrationId, _csvPath, "a.csv", null, CancellationToken.None);
        var again = await service.IngestUploadedFileAsync(_integrationId, _csvPath, "a-copy.csv", null, CancellationToken.None);

        var other = Path.GetTempFileName();
        try
        {
            File.WriteAllText(other, SampleCsv + "T6,E-9,2026-10-04 10:00:00,IN,DEV-1,\r\n");
            await service.IngestUploadedFileAsync(_integrationId, other, "b.csv", null, CancellationToken.None);
        }
        finally
        {
            File.Delete(other);
        }

        Assert.Null(first!.SameFileUploadedAt);
        Assert.NotNull(again!.SameFileUploadedAt);

        // The repeat's rows are stored too (invariant 1): 5 + 5 + 6.
        Assert.Equal(16, db.RawAttendanceTransactions.Count());

        // Newest first; only the second upload of the same bytes is a repeat.
        var history = await service.ListUploadedFilesAsync(null, 50, CancellationToken.None);
        Assert.Equal(["b.csv", "a-copy.csv", "a.csv"], history.Select(h => h.FileName));
        Assert.Equal([false, true, false], history.Select(h => h.RepeatsAnEarlierUpload));
        Assert.All(history, h => Assert.Equal("HQ uploads", h.IntegrationName));
        Assert.Equal(history[1].Sha256, history[2].Sha256);
    }

    [Fact]
    public async Task AWebhookBody_IsNotTrackedAsAnUploadedFile()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await CreateService(db).IngestWebhookPayloadAsync(_integrationId, _csvPath, CancellationToken.None);

        Assert.Empty(db.AttendanceUploadedFiles);
    }

    [Fact]
    public async Task TheUploadRecord_CanNotBeChangedOrDeleted()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        await CreateService(db).IngestUploadedFileAsync(_integrationId, _csvPath, "a.csv", null, CancellationToken.None);

        var file = db.AttendanceUploadedFiles.Single();
        file.FileName = "tampered.csv";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        db.AttendanceUploadedFiles.Remove(db.AttendanceUploadedFiles.Single());
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    private int SetDeviceForUploads(string timeZoneId, AttendanceTransport transport)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var device = new AttendanceDevice { IntegrationId = integration.Id, ExternalDeviceId = "upload", Name = "Uploads", TimeZoneId = timeZoneId };
        db.AttendanceDevices.Add(device);
        db.SaveChanges();
        integration.DefaultDeviceId = device.Id;
        integration.Transport = transport;
        db.SaveChanges();
        return device.Id;
    }

    private IngestionService CreateService(RobeemDbContext db) =>
        new(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _adapters);
}
