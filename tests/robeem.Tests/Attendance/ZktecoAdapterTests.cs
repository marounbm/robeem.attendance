using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

public sealed class ZktecoAdapterTests : IDisposable
{
    // The headers and value shapes of the real export (tests/Attendance data April and May 2026.xls),
    // plus a CardNo that is filled in and a column the allowlist does not know.
    private const string SampleCsv =
        "Department,Name,No.,Date/Time,Status,Location ID,ID Number,VerifyCode,CardNo,Extra\r\n" +
        "1 - Marketing,Hiba Rahme,21001,01-Apr-26 08:33:25,C/In,1,21001,Fingerpint,0099887,keep-out\r\n" +
        "1 - Marketing,Hiba Rahme,21001,01-Apr-26 17:35:02,C/Out,1,21001,Fingerpint,,\r\n" +
        "2 - Sales,Yara Jabbour,,01-Apr-26 18:00:00,OT In,1,22002,Fingerpint,,\r\n" +
        "2 - Sales,Charbel El Khoury,22001,not a date,C/In,1,22001,Fingerpint,,\r\n" +
        "2 - Sales,Charbel El Khoury,22001,02-Apr-26 08:00:00,Lunch,1,22001,Fingerpint,,\r\n";

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddKeyedSingleton<IAttendanceProvider, ZktecoProvider>(ZktecoProvider.ProviderKey)
        .AddKeyedSingleton<IAttendanceEventInterpreter, ZktecoInterpreter>(ZktecoProvider.ProviderKey)
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _hibaId;

    public ZktecoAdapterTests()
    {
        // Excel writes a UTF-8 byte order mark when it saves as CSV.
        File.WriteAllText(_csvPath, SampleCsv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var hiba = new Profile { EmployeeNumber = "21001", FirstName = "Hiba", LastName = "Rahme", Title = "Hiba Rahme" };
        var integration = new AttendanceIntegration
        {
            Provider = new AttendanceProvider { ProviderKey = ZktecoProvider.ProviderKey, DisplayName = "ZKTeco export" },
            DisplayName = "Head office clock",
        };
        db.AddRange(
            new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "U350", TimeZoneId = "Asia/Beirut" },
            new ExternalEmployeeIdMapping { Integration = integration, ExternalEmployeeId = "21001", Employee = hiba },
            new ExternalEmployeeIdMapping
            {
                Integration = integration,
                ExternalEmployeeId = "22002",
                Employee = new Profile { EmployeeNumber = "22002", FirstName = "Yara", LastName = "Jabbour", Title = "Yara Jabbour" },
            });
        db.SaveChanges();
        _integrationId = integration.Id;
        _hibaId = hiba.Id;
    }

    public void Dispose()
    {
        File.Delete(_csvPath);
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task Upload_StoresEveryRow_WithTheVendorsFieldsMapped()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var result = await CreateIngestion(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(5, result.RecordsIngested);
        Assert.Equal(1, result.RecordsFailed);

        var rows = db.RawAttendanceTransactions.OrderBy(r => r.Id).ToList();
        Assert.Equal(5, rows.Count);

        var first = rows[0];
        Assert.Equal("21001", first.ExternalEmployeeId);
        Assert.Equal("1", first.DeviceExternalId);
        Assert.Equal("C/In", first.TransactionTypeRaw);
        Assert.Equal("01-Apr-26 08:33:25", first.TransactionTimestampRaw);

        // The device is in Beirut, UTC+3 in April: the wall-clock time is its local time.
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 5, 33, 25, TimeSpan.Zero), first.TransactionTimestampParsed);

        // The employee falls back to ID Number when No. is empty.
        Assert.Equal("22002", rows[2].ExternalEmployeeId);

        // The unreadable date is stored and marked failed (invariant 1), not dropped.
        Assert.Equal(RawTransactionProcessingStatus.Failed, rows[3].ProcessingStatus);
        Assert.Null(rows[3].TransactionTimestampParsed);
    }

    [Fact]
    public async Task Upload_KeepsOnlyAllowlistedColumnsInThePayload()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        await CreateIngestion(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None);

        // Invariant 10: a column the adapter does not list never reaches the database, CardNo included.
        var payload = JsonDocument.Parse(db.RawAttendanceTransactions.OrderBy(r => r.Id).First().RawPayload).RootElement;
        var names = payload.EnumerateObject().Select(p => p.Name).Order().ToList();
        Assert.Equal(["Date/Time", "Department", "ID Number", "Location ID", "Name", "No.", "Status", "VerifyCode"], names);
        Assert.DoesNotContain("0099887", payload.GetRawText());
        Assert.DoesNotContain("keep-out", payload.GetRawText());
    }

    [Fact]
    public async Task Normalization_MapsTheStatusLabels_AndFailsAnUnknownOne()
    {
        int batchId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            batchId = (await CreateIngestion(db).IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None))!.BatchId;
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var normalization = new NormalizationService(
                db,
                new NormalizedTransactionRecorder(db),
                new AttendancePolicyResolver(db),
                _services,
                NullLogger<NormalizationService>.Instance);
            await normalization.NormalizeBatchAsync(batchId, CancellationToken.None);
        }

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var normalized = check.NormalizedAttendanceTransactions.OrderBy(n => n.RawTransactionId).ToList();

        // The row with a bad date and the row with an unknown status never become normalized rows.
        Assert.Equal(
            [NormalizedEventType.CheckIn, NormalizedEventType.CheckOut, NormalizedEventType.Other],
            normalized.Select(n => n.NormalizedEventType));
        Assert.Equal(_hibaId, normalized[0].EmployeeId);
        Assert.Equal(new DateTime(2026, 4, 1, 8, 33, 25), normalized[0].EventTimestampLocal);
        Assert.Equal("C/In", normalized[0].OriginalEventType);

        var failed = check.RawAttendanceTransactions.Where(r => r.ProcessingStatus == RawTransactionProcessingStatus.Failed).OrderBy(r => r.Id).ToList();
        Assert.Equal(2, failed.Count);
        Assert.Contains("Unparseable timestamp", failed[0].ProcessingError);
        Assert.Contains("Unrecognized event type 'Lunch'", failed[1].ProcessingError);
    }

    [Fact]
    public async Task PullingFromTheDevice_IsNotSupported_SoTheSyncJobSkipsIt()
    {
        var provider = _services.GetRequiredKeyedService<IAttendanceProvider>(ZktecoProvider.ProviderKey);
        var config = new AttendanceIntegrationConfig(_integrationId, ZktecoProvider.ProviderKey, string.Empty);

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetTransactionChangesAsync(config, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("01-Apr-26 08:33:25", 2026, 4, 1, 8, 33, 25)]
    [InlineData("1-Apr-26 08:33", 2026, 4, 1, 8, 33, 0)]
    [InlineData("01-Apr-2026 08:33:25", 2026, 4, 1, 8, 33, 25)]
    [InlineData("01/04/2026 08:33", 2026, 4, 1, 8, 33, 0)]
    [InlineData("31/03/2026 23:59:59", 2026, 3, 31, 23, 59, 59)]
    [InlineData("2026-04-01 08:33:25", 2026, 4, 1, 8, 33, 25)]
    public void Interpreter_ReadsTheDayFirstFormats_AsDeviceWallClock(string text, int year, int month, int day, int hour, int minute, int second)
    {
        var parsed = new ZktecoInterpreter().ParseTimestamp(text);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), parsed.Value.WallClock);
        Assert.Null(parsed.Value.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("13/13/2026 08:00")]
    public void Interpreter_DoesNotGuess_AtAnUnreadableTimestamp(string text) =>
        Assert.Null(new ZktecoInterpreter().ParseTimestamp(text));

    [Theory]
    [InlineData("C/In", NormalizedEventType.CheckIn)]
    [InlineData("c/out", NormalizedEventType.CheckOut)]
    [InlineData(" Break Out ", NormalizedEventType.BreakOut)]
    [InlineData("BREAK-IN", NormalizedEventType.BreakIn)]
    [InlineData("OT In", NormalizedEventType.Other)]
    [InlineData("OT Out", NormalizedEventType.Other)]
    public void Interpreter_MapsTheStatusLabels(string label, NormalizedEventType expected) =>
        Assert.Equal(expected, new ZktecoInterpreter().MapEventType(label, null));

    [Fact]
    public void Interpreter_ReturnsNull_ForALabelItDoesNotKnow() =>
        Assert.Null(new ZktecoInterpreter().MapEventType("Lunch", null));

    [Fact]
    public void Guide_ListsTheCodesAndFormats_TheInterpreterUses_AndWhatIsNotStored()
    {
        var guide = new ZktecoProvider().DescribeMapping();

        Assert.Equal(ZktecoProvider.ProviderKey, guide.ProviderKey);
        Assert.Contains(guide.EventCodes, c => c is { Code: "C/In", EventType: nameof(NormalizedEventType.CheckIn) });
        Assert.Equal(ZktecoInterpreter.EventCodes.Count, guide.EventCodes.Count);
        Assert.Equal(ZktecoInterpreter.TimestampFormats, guide.TimestampFormats);
        Assert.Contains("CardNo", guide.NotStored);
    }

    private IngestionService CreateIngestion(RobeemDbContext db) =>
        new(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services);
}
