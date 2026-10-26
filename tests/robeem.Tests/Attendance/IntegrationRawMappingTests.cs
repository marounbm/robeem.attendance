using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
using robeem.Contracts.Common;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>The raw to normalized view of an integration (CLAUDE.md §2.8: a direct read of raw attendance is audited).</summary>
public sealed class IntegrationRawMappingTests : IDisposable
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
        .AddKeyedSingleton<IAttendanceAdapter, UndescribedProvider>("undescribed")
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _otherIntegrationId;
    private readonly int _batchId;

    public IntegrationRawMappingTests()
    {
        File.WriteAllText(_csvPath, Csv);
        _database.SeedAttendanceCatalog();

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var zkteco = AttendanceTestData.Seeded(db, "zkteco");
            var integration = new AttendanceIntegration { Connector = zkteco, DisplayName = "Head office clock" };
            var other = new AttendanceIntegration { Connector = AttendanceTestData.Connector("undescribed", "Undescribed", adapterKey: "undescribed"), DisplayName = "Other" };
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
    public async Task EachRawRow_ShowsBesideTheNormalizedRowItProduced_NewestFirst()
    {
        var rows = await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 0));

        Assert.NotNull(rows);
        Assert.Equal(["Failed", "Failed", "Normalized", "Normalized", "Normalized"], rows.Select(r => r.RawStatus));
        Assert.All(rows, r => Assert.Equal(("zkteco", _integrationId, _batchId), (r.VendorKey, r.IntegrationId, r.BatchId)));

        // Oldest raw row: the vendor's text on the left, the interpreted punch on the right.
        var first = rows[4];
        Assert.Equal(("21001", "1", "C/In", "01-Apr-26 08:33:25"), (first.ExternalEmployeeId, first.DeviceExternalId, first.TypeRaw, first.TimestampRaw));
        Assert.Equal(("CheckIn", "Certain", "Unique", "None"), (first.EventType, first.Confidence, first.DuplicateStatus, first.ExceptionStatus));
        Assert.Equal(("21001", "Hiba Rahme", "U350"), (first.EmployeeNumber, first.EmployeeName, first.DeviceName));
        Assert.Equal((new DateTime(2026, 4, 1, 8, 33, 25), "Asia/Beirut"), (first.LocalTime, first.TimeZoneId));
        Assert.NotNull(first.NormalizedId);

        // An overtime punch is kept as Other, not guessed as a check-in.
        Assert.Equal(("OT In", "Other", "Yara Jabbour"), (rows[2].TypeRaw, rows[2].EventType, rows[2].EmployeeName));
    }

    [Fact]
    public async Task AReplacedPunch_IsNotShownBesideItsRawRow()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).First().ProcessingStatus = NormalizedTransactionProcessingStatus.Excluded;
            db.SaveChanges();
        }

        var rows = (await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 0)))!;

        Assert.Equal(5, rows.Count);
        Assert.Null(rows[4].NormalizedId);
        Assert.NotNull(rows[3].NormalizedId);
    }

    [Fact]
    public async Task ARowThatFailed_HasNoNormalizedSide_AndSaysWhy()
    {
        var rows = (await ListAsync(_integrationId, new RawNormalizedFilter(null, RawTransactionProcessingStatus.Failed, 1, 0)))!;

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.NormalizedId));
        Assert.All(rows, r => Assert.Null(r.EventType));
        Assert.Contains("Unrecognized event type 'Lunch'", rows[0].ProcessingError);
        Assert.Contains("Unparseable timestamp", rows[1].ProcessingError);
    }

    [Fact]
    public async Task ThePayload_IsTheAllowlistedOneAsStored_WithoutTheCardNumber()
    {
        var first = (await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 0)))![4];

        Assert.Contains("\"Name\":\"Hiba Rahme\"", first.Payload);
        Assert.DoesNotContain("0099887", first.Payload);
        Assert.DoesNotContain("CardNo", first.Payload);
    }

    [Fact]
    public async Task TheListIsNarrowedByBatch_AndLimited()
    {
        Assert.Equal(5, (await ListAsync(_integrationId, new RawNormalizedFilter(_batchId, null, 1, 0)))!.Count);
        Assert.Empty((await ListAsync(_integrationId, new RawNormalizedFilter(_batchId + 100, null, 1, 0)))!);
    }

    [Fact]
    public async Task TheList_IsPaged_NewestFirst_WithTheTotalOfEveryPage()
    {
        var one = (await PageAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 2)))!;
        var two = (await PageAsync(_integrationId, new RawNormalizedFilter(null, null, 2, 2)))!;
        var three = (await PageAsync(_integrationId, new RawNormalizedFilter(null, null, 3, 2)))!;

        Assert.Equal((5, 2, 2), (one.Total, one.PageSize, one.Items.Count));
        Assert.Equal((2, 1), (two.Page, three.Items.Count));
        Assert.Equal(5, one.Items.Concat(two.Items).Concat(three.Items).Select(r => r.RawId).Distinct().Count());
        Assert.True(one.Items[0].RawId > one.Items[1].RawId);
        Assert.True(one.Items[1].RawId > two.Items[0].RawId);
    }

    [Fact]
    public async Task TheTotal_FollowsTheFilters_AndAPagePastTheEnd_AnswersTheLastPage()
    {
        var failed = (await PageAsync(_integrationId, new RawNormalizedFilter(null, RawTransactionProcessingStatus.Failed, 9, 1)))!;

        Assert.Equal((2, 2, 1), (failed.Total, failed.Page, failed.Items.Count));
        Assert.Equal(20, (await PageAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 0)))!.PageSize);
        Assert.Equal(200, (await PageAsync(_integrationId, new RawNormalizedFilter(null, null, 1, 5000)))!.PageSize);
    }

    [Fact]
    public async Task EveryPageRead_IsAuditedWithItsPageAndSize()
    {
        await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 2, 2));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var entry = Assert.Single(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead).ToList());
        Assert.Contains("page 2, 2 per page", entry.Details);
    }

    [Fact]
    public async Task AnotherIntegrationsRows_NeverShow()
    {
        Assert.Empty((await ListAsync(_otherIntegrationId, new RawNormalizedFilter(null, null, 1, 0)))!);
    }

    [Fact]
    public async Task EveryRead_IsOnTheAuditLog_BeforeTheDataIsReturned()
    {
        await ListAsync(_integrationId, new RawNormalizedFilter(null, RawTransactionProcessingStatus.Failed, 1, 10));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var entry = Assert.Single(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead).ToList());
        Assert.Equal(_integrationId, entry.TargetId);
        Assert.Contains(nameof(RawAttendanceTransaction), entry.TargetType);
        Assert.Contains(nameof(NormalizedAttendanceTransaction), entry.TargetType);
        Assert.Contains("status Failed", entry.Details);
    }

    [Fact]
    public async Task AnUnknownIntegration_IsNull_AndLeavesNothingOnTheAuditLog()
    {
        Assert.Null(await ListAsync(_integrationId + 100, new RawNormalizedFilter(null, null, 1, 0)));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead));
    }

    [Fact]
    public async Task TheGuide_IsTheFormatsOrTheAdaptersOwnDescription_OrSaysThereIsNone()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);

        var guide = await service.GetRawMappingGuideAsync(_integrationId, CancellationToken.None);
        var undescribed = await service.GetRawMappingGuideAsync(_otherIntegrationId, CancellationToken.None);

        Assert.Equal("zkteco", guide!.VendorKey);
        Assert.Contains(guide.EventCodes, c => c.Code == "C/In");
        Assert.Equal("undescribed", undescribed!.VendorKey);
        Assert.Contains("does not describe", undescribed.Summary);
        Assert.Null(await service.GetRawMappingGuideAsync(_integrationId + 100, CancellationToken.None));
    }

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.RawNormalizedMappingItem>?> ListAsync(int integrationId, RawNormalizedFilter filter) =>
        (await PageAsync(integrationId, filter))?.Items;

    private async Task<PagedResult<robeem.Contracts.Attendance.RawNormalizedMappingItem>?> PageAsync(int integrationId, RawNormalizedFilter filter)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ListRawToNormalizedAsync(integrationId, filter, CancellationToken.None);
    }

    private AttendanceIntegrationService CreateService(RobeemDbContext db) =>
        new(db, new NoSync(), new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), _services, new StubAdapterCatalog());

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>An adapter that does not implement <see cref="IAttendanceMappingGuide"/>.</summary>
    private sealed class UndescribedProvider : IAttendanceAdapter
    {
        public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
