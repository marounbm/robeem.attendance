using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

namespace robeem.Tests.Attendance;

/// <summary>The global raw and normalized lists (CLAUDE.md §2.8: a direct read of raw or normalized attendance is audited).</summary>
public sealed class AttendanceTransactionQueryTests : IDisposable
{
    private const string Csv =
        "Department,Name,No.,Date/Time,Status,Location ID,ID Number,VerifyCode,CardNo\r\n" +
        "1 - Marketing,Hiba Rahme,21001,01-Apr-26 08:33:25,C/In,1,21001,Fingerpint,0099887\r\n" +
        "1 - Marketing,Hiba Rahme,21001,01-Apr-26 17:35:02,C/Out,1,21001,Fingerpint,\r\n" +
        "2 - Sales,Yara Jabbour,22002,01-Apr-26 18:00:00,OT In,1,22002,Fingerpint,\r\n" +
        "2 - Sales,Charbel El Khoury,22001,not a date,C/In,1,22001,Fingerpint,\r\n" +
        "2 - Sales,Charbel El Khoury,22001,02-Apr-26 08:00:00,Lunch,1,22001,Fingerpint,\r\n";

    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton<ICustomFormatFileReader, CustomFormatFileReader>()
        .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _otherIntegrationId;
    private readonly int _batchId;

    public AttendanceTransactionQueryTests()
    {
        File.WriteAllText(_csvPath, Csv);
        _database.SeedAttendanceCatalog();

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var integration = new AttendanceIntegration { Connector = AttendanceTestData.Seeded(db, "zkteco"), DisplayName = "Head office clock" };
            var other = new AttendanceIntegration { Connector = AttendanceTestData.Connector("other", "Other"), DisplayName = "Branch clock" };
            var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "U350", TimeZoneId = "Asia/Beirut" };
            db.AddRange(
                other,
                device,
                new ExternalEmployeeIdMapping
                {
                    Device = device,
                    ExternalEmployeeId = "21001",
                    Employee = new Profile { EmployeeNumber = "21001", FirstName = "Hiba", LastName = "Rahme", Title = "Hiba Rahme" },
                },
                new ExternalEmployeeIdMapping
                {
                    Device = device,
                    ExternalEmployeeId = "22002",
                    Employee = new Profile { EmployeeNumber = "22002", FirstName = "Yara", LastName = "Jabbour", Title = "Yara Jabbour" },
                });
            db.SaveChanges();
            _integrationId = integration.Id;
            _otherIntegrationId = other.Id;
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            _batchId = new IngestionService(db, new RawPayloadSanitizer(Options.Create(new RawPayloadSanitizerOptions())), _services)
                .IngestManualUploadAsync(_integrationId, _csvPath, CancellationToken.None).GetAwaiter().GetResult()!.BatchId;
        }

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            new NormalizationService(db, new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), _services, NullLogger<NormalizationService>.Instance)
                .NormalizeBatchAsync(_batchId, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public void Dispose()
    {
        File.Delete(_csvPath);
        _services.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task RawPunches_ListNewestFirst_EachLinkedToTheNormalizedPunchItBecame()
    {
        var rows = await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 0));

        Assert.Equal(["Failed", "Failed", "Normalized", "Normalized", "Normalized"], rows.Select(r => r.Status));
        Assert.True(rows.Zip(rows.Skip(1), (a, b) => a.Id > b.Id).All(ordered => ordered));
        Assert.All(rows, r => Assert.Equal(("Head office clock", _batchId), (r.IntegrationName, r.BatchId)));

        var first = rows[4];
        Assert.Equal(("21001", "1", "C/In", "01-Apr-26 08:33:25"), (first.ExternalEmployeeId, first.DeviceExternalId, first.TypeRaw, first.TimestampRaw));
        Assert.NotNull(first.NormalizedId);
        Assert.All(rows.Take(2), r => Assert.Null(r.NormalizedId));
        Assert.Contains("Unrecognized event type 'Lunch'", rows[0].ProcessingError);
    }

    [Fact]
    public async Task ThePayload_IsTheAllowlistedOneAsStored_WithoutTheCardNumber()
    {
        var first = (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 0)))[4];

        Assert.Contains("\"Name\":\"Hiba Rahme\"", first.Payload);
        Assert.DoesNotContain("0099887", first.Payload);
        Assert.DoesNotContain("CardNo", first.Payload);
    }

    [Fact]
    public async Task RawPunches_AreNarrowedByStatusBatchIntegrationDateAndSearch()
    {
        Assert.Equal(2, (await ListRawAsync(new RawTransactionFilter(null, null, RawTransactionProcessingStatus.Failed, null, null, null, 0))).Count);
        Assert.Equal(5, (await ListRawAsync(new RawTransactionFilter(_integrationId, _batchId, null, null, null, null, 0))).Count);
        Assert.Empty(await ListRawAsync(new RawTransactionFilter(null, _batchId + 100, null, null, null, null, 0)));
        Assert.Empty(await ListRawAsync(new RawTransactionFilter(_otherIntegrationId, null, null, null, null, null, 0)));

        // The vendor's ids, found by part and whatever the case.
        Assert.Equal(3, (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, " 2200 ", 0))).Count);
        Assert.Equal(2, (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, "2100", 0))).Count);
        Assert.Empty(await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, "nobody", 0)));

        // The batch ran today: a range ending yesterday finds nothing, one that includes today everything.
        Assert.Empty(await ListRawAsync(new RawTransactionFilter(null, null, null, null, DateTime.Today.AddDays(-1), null, 0)));
        Assert.Equal(5, (await ListRawAsync(new RawTransactionFilter(null, null, null, DateTime.Today, DateTime.Today, null, 0))).Count);
    }

    [Fact]
    public async Task RawPunches_AreLimited_AndTheLimitIsClamped()
    {
        Assert.Equal(2, (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 2))).Count);
        Assert.Equal(5, (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 100000))).Count);
        Assert.Equal(5, (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, -5))).Count);
    }

    [Fact]
    public async Task NormalizedPunches_ListNewestPunchFirst_WithTheEmployeeDeviceAndInterpretation()
    {
        var rows = await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, null, null, null, null, 0));

        Assert.Equal(["Other", "CheckOut", "CheckIn"], rows.Select(r => r.EventType));
        Assert.Equal(("Yara Jabbour", "22002", "OT In"), (rows[0].EmployeeName, rows[0].EmployeeNumber, rows[0].OriginalEventType));

        var checkIn = rows[2];
        Assert.Equal(("Head office clock", "Hiba Rahme", "U350"), (checkIn.IntegrationName, checkIn.EmployeeName, checkIn.DeviceName));
        Assert.Equal((new DateTime(2026, 4, 1, 8, 33, 25), new DateTime(2026, 4, 1), "Asia/Beirut"), (checkIn.LocalTime, checkIn.AttendanceDate, checkIn.TimeZoneId));
        Assert.Equal(("Certain", "Unique", "None"), (checkIn.Confidence, checkIn.DuplicateStatus, checkIn.ExceptionStatus));
        Assert.Null(checkIn.DuplicateOfId);
    }

    [Fact]
    public async Task NormalizedPunches_AreNarrowedByEmployeeEventDuplicateFlagRawIdIntegrationAndDate()
    {
        Assert.Equal(2, (await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, "HIBA", null, null, null, null, null, 0))).Count);
        Assert.Single(await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, "22002", null, null, null, null, null, 0)));
        Assert.Single(await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, NormalizedEventType.Other, null, null, null, null, 0)));
        Assert.Equal(3, (await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, DuplicateStatus.Unique, TransactionExceptionStatus.None, null, null, 0))).Count);
        Assert.Empty(await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, DuplicateStatus.DuplicateConfirmed, null, null, null, 0)));
        Assert.Empty(await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, null, TransactionExceptionStatus.FlaggedInvalidSequence, null, null, 0)));
        Assert.Empty(await ListNormalizedAsync(new NormalizedTransactionFilter(_otherIntegrationId, null, null, null, null, null, null, null, 0)));
        Assert.Equal(3, (await ListNormalizedAsync(new NormalizedTransactionFilter(_integrationId, null, null, null, null, null, new DateTime(2026, 4, 1), new DateTime(2026, 4, 1, 23, 0, 0), 0))).Count);
        Assert.Empty(await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, null, null, new DateTime(2026, 4, 2), null, 0)));

        var any = (await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 0))).Last(r => r.NormalizedId is not null);
        var byRaw = Assert.Single(await ListNormalizedAsync(new NormalizedTransactionFilter(null, any.Id, null, null, null, null, null, null, 0)));
        Assert.Equal((any.NormalizedId, any.Id), (byRaw.Id, byRaw.RawId));
    }

    [Fact]
    public async Task AReplacedPunch_IsHistory_NotTheOneItsRawPunchLinksTo()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).First().ProcessingStatus = NormalizedTransactionProcessingStatus.Excluded;
            db.SaveChanges();
        }

        var raw = await ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 0));
        var normalized = await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, null, null, null, null, 0));

        Assert.Null(raw[4].NormalizedId);
        Assert.NotNull(raw[3].NormalizedId);
        Assert.Equal(3, normalized.Count);
        Assert.Single(normalized, n => n.ProcessingStatus == "Excluded");
    }

    [Fact]
    public async Task TheIntegrations_AreListedByName()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var options = await new AttendanceTransactionQuery(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System)).ListIntegrationsAsync(CancellationToken.None);

        Assert.Equal(["Branch clock", "Head office clock"], options.Select(o => o.Name));
        Assert.Empty(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead));
    }

    [Fact]
    public async Task EveryList_IsOnTheAuditLog_NamingWhatWasRead()
    {
        await ListRawAsync(new RawTransactionFilter(null, null, RawTransactionProcessingStatus.Failed, null, null, "2200", 10));
        await ListNormalizedAsync(new NormalizedTransactionFilter(null, null, "hiba", NormalizedEventType.CheckIn, null, null, null, null, 10));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var entries = db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead).OrderBy(a => a.Id).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(nameof(RawAttendanceTransaction), entries[0].TargetType);
        Assert.Contains("status Failed", entries[0].Details);
        Assert.Contains("search '2200'", entries[0].Details);
        Assert.Equal(nameof(NormalizedAttendanceTransaction), entries[1].TargetType);
        Assert.Contains("employee 'hiba'", entries[1].Details);
        Assert.Contains("event CheckIn", entries[1].Details);
    }

    [Fact]
    public async Task AnotherTenantsPunches_NeverShow()
    {
        using var db = _database.CreateContext(TestDatabase.TenantB);
        var query = new AttendanceTransactionQuery(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System));

        Assert.Empty(await query.ListRawAsync(new RawTransactionFilter(null, null, null, null, null, null, 0), CancellationToken.None));
        Assert.Empty(await query.ListNormalizedAsync(new NormalizedTransactionFilter(null, null, null, null, null, null, null, null, 0), CancellationToken.None));
    }

    private async Task<IReadOnlyList<RawTransactionListItem>> ListRawAsync(RawTransactionFilter filter)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceTransactionQuery(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System)).ListRawAsync(filter, CancellationToken.None);
    }

    private async Task<IReadOnlyList<NormalizedTransactionListItem>> ListNormalizedAsync(NormalizedTransactionFilter filter)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await new AttendanceTransactionQuery(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System)).ListNormalizedAsync(filter, CancellationToken.None);
    }
}
