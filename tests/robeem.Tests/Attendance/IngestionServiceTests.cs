using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
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

    private IngestionService CreateService(RobeemDbContext db) =>
        new(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _adapters);
}
