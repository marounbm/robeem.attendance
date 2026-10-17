using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// The ZKTeco export, read by the seeded "ZKTeco attendance export" file format. These are the tests of the
/// hard-coded ZKTeco adapter, ported: the same files must come out the same way.
/// </summary>
public sealed class ZktecoFormatTests : IDisposable
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
        .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
        .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _hibaId;

    public ZktecoFormatTests()
    {
        // Excel writes a UTF-8 byte order mark when it saves as CSV.
        File.WriteAllText(_csvPath, SampleCsv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _database.SeedAttendanceCatalog();

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var hiba = new Profile { EmployeeNumber = "21001", FirstName = "Hiba", LastName = "Rahme", Title = "Hiba Rahme" };
        var integration = new AttendanceIntegration
        {
            Provider = db.AttendanceProviders.Single(p => p.ProviderKey == "zkteco"),
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

        // Invariant 10: a column the format does not list never reaches the database, CardNo included.
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
    public void ZktecoNeedsNoCodeAdapter_SoNothingIsRegisteredForItsKey()
    {
        // A vendor of exported files is data: a file format and a provider row, no adapter and no interpreter.
        Assert.Null(_services.GetKeyedService<IAttendanceProvider>("zkteco"));
        Assert.Null(_services.GetKeyedService<IAttendanceEventInterpreter>("zkteco"));
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
        var parsed = Interpreter().ParseTimestamp(text);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), parsed.Value.WallClock);
        Assert.Null(parsed.Value.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("13/13/2026 08:00")]
    public void Interpreter_DoesNotGuess_AtAnUnreadableTimestamp(string text) =>
        Assert.Null(Interpreter().ParseTimestamp(text));

    [Theory]
    [InlineData("C/In", NormalizedEventType.CheckIn)]
    [InlineData("c/out", NormalizedEventType.CheckOut)]
    [InlineData(" Break Out ", NormalizedEventType.BreakOut)]
    [InlineData("BREAK-IN", NormalizedEventType.BreakIn)]
    [InlineData("OT In", NormalizedEventType.Other)]
    [InlineData("OT Out", NormalizedEventType.Other)]
    public void Interpreter_MapsTheStatusLabels(string label, NormalizedEventType expected) =>
        Assert.Equal(expected, Interpreter().MapEventType(label, null));

    [Fact]
    public void Interpreter_ReturnsNull_ForALabelItDoesNotKnow() =>
        Assert.Null(Interpreter().MapEventType("Lunch", null));

    [Fact]
    public void Guide_ListsTheCodesAndFormats_TheFormatUses_AndWhatIsNotStored()
    {
        var guide = FileFormatLayout.Describe(Format(), "zkteco");

        Assert.Equal("zkteco", guide.ProviderKey);
        Assert.Contains(guide.EventCodes, c => c is { Code: "C/In", EventType: nameof(NormalizedEventType.CheckIn) });
        Assert.Contains(guide.EventCodes, c => c is { Code: "OT In", EventType: nameof(NormalizedEventType.Other) });
        Assert.Contains("d-MMM-yy H:mm:ss", guide.TimestampFormats);
        Assert.Contains(guide.NotStored, n => n.Contains("not listed", StringComparison.Ordinal));
        Assert.Contains(guide.Fields, f => f is { Target: "ExternalEmployeeId" } && f.Source == "No. / ID Number");
    }

    [Fact]
    public async Task TheRealExport_IsReadWithItsColumnsInFileOrder_MinusCardNo()
    {
        var xls = Path.Combine(AppContext.BaseDirectory, "TestData", "ZktecoExport.xls");

        var transactions = await new CustomFormatFileReader().ReadAsync(Format(), xls, CancellationToken.None);

        // The payload keeps the file's columns in file order, minus CardNo, which is never copied.
        var columns = JsonDocument.Parse(transactions[0].RawPayload).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["Department", "Name", "No.", "Date/Time", "Status", "Location ID", "ID Number", "VerifyCode"], columns);
        Assert.All(transactions, t => Assert.True(
            DateTime.TryParseExact(t.TransactionTimestampRaw, "dd-MMM-yy HH:mm:ss", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.None, out _),
            t.TransactionTimestampRaw));
        Assert.All(transactions, t => Assert.Contains(t.TransactionTypeRaw, new[] { "C/In", "C/Out" }));
    }

    private AttendanceFileFormat Format()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return db.AttendanceFileFormats.Include(f => f.EventCodes).AsNoTracking().Single(f => f.Name == "ZKTeco attendance export");
    }

    private CustomFormatInterpreter Interpreter() => new(Format());

    private IngestionService CreateIngestion(RobeemDbContext db) =>
        new(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services);
}
