using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using robeem.Application.Attendance;
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
        .AddKeyedSingleton<IAttendanceProvider, ZktecoProvider>(ZktecoProvider.ProviderKey)
        .AddKeyedSingleton<IAttendanceEventInterpreter, ZktecoInterpreter>(ZktecoProvider.ProviderKey)
        .AddKeyedSingleton<IAttendanceProvider, UndescribedProvider>("undescribed")
        .BuildServiceProvider();

    private readonly string _csvPath = Path.GetTempFileName();
    private readonly int _integrationId;
    private readonly int _otherIntegrationId;
    private readonly int _batchId;

    public IntegrationRawMappingTests()
    {
        File.WriteAllText(_csvPath, Csv);

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var zkteco = new AttendanceProvider { ProviderKey = ZktecoProvider.ProviderKey, DisplayName = "ZKTeco export" };
            var integration = new AttendanceIntegration { Provider = zkteco, DisplayName = "Head office clock" };
            var other = new AttendanceIntegration { Provider = new AttendanceProvider { ProviderKey = "undescribed", DisplayName = "Undescribed" }, DisplayName = "Other" };
            db.AddRange(
                other,
                new AttendanceDevice { Integration = integration, ExternalDeviceId = "1", Name = "U350", TimeZoneId = "Asia/Beirut" },
                new ExternalEmployeeIdMapping
                {
                    Integration = integration,
                    ExternalEmployeeId = "21001",
                    Employee = new Profile { EmployeeNumber = "21001", FirstName = "Hiba", LastName = "Rahme", Title = "Hiba Rahme" },
                },
                new ExternalEmployeeIdMapping
                {
                    Integration = integration,
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
        var rows = await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 0));

        Assert.NotNull(rows);
        Assert.Equal(["Failed", "Failed", "Normalized", "Normalized", "Normalized"], rows.Select(r => r.RawStatus));
        Assert.All(rows, r => Assert.Equal((ZktecoProvider.ProviderKey, _integrationId, _batchId), (r.ProviderKey, r.IntegrationId, r.BatchId)));

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

        var rows = (await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 0)))!;

        Assert.Equal(5, rows.Count);
        Assert.Null(rows[4].NormalizedId);
        Assert.NotNull(rows[3].NormalizedId);
    }

    [Fact]
    public async Task ARowThatFailed_HasNoNormalizedSide_AndSaysWhy()
    {
        var rows = (await ListAsync(_integrationId, new RawNormalizedFilter(null, RawTransactionProcessingStatus.Failed, 0)))!;

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.NormalizedId));
        Assert.All(rows, r => Assert.Null(r.EventType));
        Assert.Contains("Unrecognized event type 'Lunch'", rows[0].ProcessingError);
        Assert.Contains("Unparseable timestamp", rows[1].ProcessingError);
    }

    [Fact]
    public async Task ThePayload_IsTheAllowlistedOneAsStored_WithoutTheCardNumber()
    {
        var first = (await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 0)))![4];

        Assert.Contains("\"Name\":\"Hiba Rahme\"", first.Payload);
        Assert.DoesNotContain("0099887", first.Payload);
        Assert.DoesNotContain("CardNo", first.Payload);
    }

    [Fact]
    public async Task TheListIsNarrowedByBatch_AndLimited()
    {
        Assert.Equal(5, (await ListAsync(_integrationId, new RawNormalizedFilter(_batchId, null, 0)))!.Count);
        Assert.Empty((await ListAsync(_integrationId, new RawNormalizedFilter(_batchId + 100, null, 0)))!);

        var limited = (await ListAsync(_integrationId, new RawNormalizedFilter(null, null, 2)))!;
        Assert.Equal(2, limited.Count);
        Assert.True(limited[0].RawId > limited[1].RawId);
    }

    [Fact]
    public async Task AnotherIntegrationsRows_NeverShow()
    {
        Assert.Empty((await ListAsync(_otherIntegrationId, new RawNormalizedFilter(null, null, 0)))!);
    }

    [Fact]
    public async Task EveryRead_IsOnTheAuditLog_BeforeTheDataIsReturned()
    {
        await ListAsync(_integrationId, new RawNormalizedFilter(null, RawTransactionProcessingStatus.Failed, 10));

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
        Assert.Null(await ListAsync(_integrationId + 100, new RawNormalizedFilter(null, null, 0)));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.AttendanceAuditLogs.Where(a => a.Action == AttendanceAuditActions.AttendanceDataRead));
    }

    [Fact]
    public async Task TheGuide_IsTheAdaptersOwnDescription_OrSaysThereIsNone()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var service = CreateService(db);

        var guide = await service.GetRawMappingGuideAsync(_integrationId, CancellationToken.None);
        var undescribed = await service.GetRawMappingGuideAsync(_otherIntegrationId, CancellationToken.None);

        Assert.Equal(ZktecoProvider.ProviderKey, guide!.ProviderKey);
        Assert.Contains(guide.EventCodes, c => c.Code == "C/In");
        Assert.Equal("undescribed", undescribed!.ProviderKey);
        Assert.Contains("does not describe", undescribed.Summary);
        Assert.Null(await service.GetRawMappingGuideAsync(_integrationId + 100, CancellationToken.None));
    }

    private async Task<IReadOnlyList<robeem.Contracts.Attendance.RawNormalizedMappingItem>?> ListAsync(int integrationId, RawNormalizedFilter filter)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).ListRawToNormalizedAsync(integrationId, filter, CancellationToken.None);
    }

    private AttendanceIntegrationService CreateService(RobeemDbContext db) =>
        new(db, new NoSync(), new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System), _services);

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>An adapter that does not implement <see cref="IAttendanceMappingGuide"/>.</summary>
    private sealed class UndescribedProvider : IAttendanceProvider
    {
        public Task<ConnectionValidationResult> ValidateConnectionAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResult> AuthenticateAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalDeviceDto>> RetrieveDevicesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExternalEmployeeDto>> RetrieveEmployeesAsync(AttendanceIntegrationConfig config, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> RetrieveAttendanceTransactionsAsync(AttendanceIntegrationConfig config, DateRange? range, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AttendanceBatchResult> GetTransactionChangesAsync(AttendanceIntegrationConfig config, SyncCursor? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
