using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.Contracts.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

/// <summary>
/// Time zones and daylight saving (P14 gap 6). A vendor sends wall-clock times; the device's IANA
/// zone turns them into instants. America/New_York changed its clocks on 2026-03-08 (02:00 to 03:00,
/// so 02:30 does not exist) and changes back on 2026-11-01 (02:00 to 01:00, so 01:30 happens twice).
/// </summary>
public sealed class TimeZoneEdgeTests : AttendanceEngineTestBase
{
    private const string NewYork = "America/New_York";

    private readonly ServiceProvider _interpreters = new ServiceCollection()
        .AddKeyedSingleton<IAttendanceEventInterpreter, GenericWebhookInterpreter>("generic-csv")
        .BuildServiceProvider();

    [Fact]
    public void ATimeInTheSpringGap_DoesNotExist()
    {
        Assert.Null(At(2026, 3, 8, 2, 30).ToUtc(NewYork));
    }

    [Fact]
    public void ATimeAroundTheSpringGap_ConvertsWithTheOffsetInForce()
    {
        // Before the change New York is UTC-5, after it UTC-4.
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero), At(2026, 3, 8, 1, 30).ToUtc(NewYork));
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero), At(2026, 3, 8, 3, 30).ToUtc(NewYork));
    }

    [Fact]
    public void ATimeInTheAutumnOverlap_IsTakenAsStandardTime()
    {
        // 01:30 happens twice and a vendor's wall-clock time does not say which. The rule is
        // .NET's: standard time (UTC-5), the later of the two instants. A punch the device took in
        // the first pass is read an hour late; it is the one case where the wall clock misleads.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), At(2026, 11, 1, 1, 30).ToUtc(NewYork));
    }

    [Fact]
    public void AnExplicitOffset_NeedsNoZone_AndTheZoneNeverOverridesIt()
    {
        var stamp = new VendorTimestamp(new DateTime(2026, 3, 8, 2, 30, 0), TimeSpan.FromHours(3));

        Assert.Equal(new DateTimeOffset(2026, 3, 7, 23, 30, 0, TimeSpan.Zero), stamp.ToUtc(null));
        Assert.Equal(stamp.ToUtc(null), stamp.ToUtc(NewYork));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not/AZone")]
    public void WithoutAnOffset_AnUnknownOrMissingZone_GivesNoInstant(string? zone)
    {
        Assert.Null(At(2026, 10, 5, 9, 0).ToUtc(zone));
    }

    [Fact]
    public async Task APunchInTheSpringGap_FailsNormalization_WithTheReason_AndIsNeverInvented()
    {
        UseDeviceZone(NewYork);
        var rawId = AddRaw("2026-03-08 02:30:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(0, 0, 0, 1), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.NormalizedAttendanceTransactions);
        var raw = db.RawAttendanceTransactions.Single(r => r.Id == rawId);
        Assert.Equal(RawTransactionProcessingStatus.Failed, raw.ProcessingStatus);
        Assert.Contains("daylight-saving gap", raw.ProcessingError);
    }

    [Fact]
    public async Task APunchInTheAutumnOverlap_IsNormalized_AsStandardTime()
    {
        UseDeviceZone(NewYork);
        AddRaw("2026-11-01 01:30:00", "IN");

        await NormalizeAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 1, 30, 0), row.EventTimestampLocal);
        Assert.Equal(new DateTime(2026, 11, 1), row.AttendanceDate);
    }

    [Fact]
    public async Task TheAttendanceDate_IsTheDeviceLocalDate_NotTheUtcOne()
    {
        UseDeviceZone("Asia/Beirut");

        // 22:30 UTC on the 5th is 01:30 on the 6th in Beirut (UTC+3 in October).
        AddRaw("2026-10-05 22:30:00Z", "IN");

        await NormalizeAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 22, 30, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal(new DateTime(2026, 10, 6, 1, 30, 0), row.EventTimestampLocal);
        Assert.Equal((new DateTime(2026, 10, 6), "Asia/Beirut"), (row.AttendanceDate, row.TimeZoneId));
    }

    [Fact]
    public async Task ADeviceInAnotherZoneThanTheServer_StillCountsTheShiftOnItsOwnClock()
    {
        // The shift is in local wall-clock time and the device is in New York: its 08:00 is 08:00 on the schedule.
        UseDeviceZone(NewYork);
        AssignSchedule(StandardDay);
        Clock.Local = Monday.At(23, 0);
        AddRaw("2026-10-05 08:00:00", "IN");
        AddRaw("2026-10-05 12:00:00", "BREAK OUT");
        AddRaw("2026-10-05 13:00:00", "BREAK IN");
        AddRaw("2026-10-05 17:00:00", "OUT");

        await NormalizeAsync();
        await ProcessAsync(Monday);

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var record = Assert.Single(db.AttendanceRecords);
        Assert.Equal((Monday.At(8, 0), Monday.At(17, 0)), (record.ActualCheckIn, record.ActualCheckOut));
        Assert.Equal((TimeSpan.FromHours(8), 0, AttendanceRecordStatus.Approved), (record.WorkingHours, record.LateMinutes, record.Status));
    }

    [Fact]
    public async Task AnUnknownDevice_WithNoIntegrationZone_FailsAsKnownToBeWaitingOnAZone()
    {
        Prepare();
        var rawId = AddUnknownDeviceRaw("2026-10-05 08:00:00");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(0, 0, 0, 1), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.NormalizedAttendanceTransactions);
        var raw = db.RawAttendanceTransactions.Single(r => r.Id == rawId);
        Assert.Equal(RawTransactionProcessingStatus.Failed, raw.ProcessingStatus);
        Assert.StartsWith(NormalizationFailures.NoTimeZonePrefix, raw.ProcessingError);
        Assert.Contains("integration has no time zone", raw.ProcessingError);
    }

    [Fact]
    public async Task AnUnknownDevice_IsReadInTheIntegrationsZone_AndStillFlagged()
    {
        Prepare();
        SetIntegrationZone("Asia/Beirut");
        AddUnknownDeviceRaw("2026-10-05 08:00:00");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(1, 0, 1, 0), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 5, 0, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal((new DateTime(2026, 10, 5, 8, 0, 0), new DateTime(2026, 10, 5), "Asia/Beirut"), (row.EventTimestampLocal, row.AttendanceDate, row.TimeZoneId));
        Assert.Equal((null, TransactionExceptionStatus.FlaggedUnrecognizedDevice), (row.DeviceId, row.ExceptionStatus));
    }

    [Fact]
    public async Task ARegisteredDevicesZone_WinsOverTheIntegrationsZone()
    {
        UseDeviceZone(NewYork);
        SetIntegrationZone("Asia/Beirut");
        AddRaw("2026-10-05 08:00:00", "IN");

        await NormalizeAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal((NewYork, TransactionExceptionStatus.None), (row.TimeZoneId, row.ExceptionStatus));
    }

    [Fact]
    public async Task APunchInTheGapOfTheIntegrationsZone_FailsWithoutBeingTakenForAMissingZone()
    {
        Prepare();
        SetIntegrationZone(NewYork);
        var rawId = AddUnknownDeviceRaw("2026-03-08 02:30:00");

        await NormalizeAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var error = db.RawAttendanceTransactions.Single(r => r.Id == rawId).ProcessingError;
        Assert.Contains("integration time zone", error);
        Assert.Contains("daylight-saving gap", error);
        Assert.DoesNotContain(NormalizationFailures.NoTimeZonePrefix, error);
    }

    [Fact]
    public async Task RetryingFailedPunches_QueuesOnlyTheOnesThatWaitedOnAZone_AndTheyNormalizeOnceAZoneIsSet()
    {
        Prepare();
        var waiting = AddUnknownDeviceRaw("2026-10-05 08:00:00");
        var broken = AddFailedRaw("Unparseable timestamp 'x'.");
        await NormalizeAsync();
        SetIntegrationZone("Asia/Beirut");

        int? queued;
        int integrationId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            integrationId = db.AttendanceIntegrations.Single().Id;
            queued = await CreateIntegrationService(db).RetryFailedAsync(integrationId, CancellationToken.None);
        }

        Assert.Equal(1, queued);
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(RawTransactionProcessingStatus.Pending, db.RawAttendanceTransactions.Single(r => r.Id == waiting).ProcessingStatus);
            Assert.Null(db.RawAttendanceTransactions.Single(r => r.Id == waiting).ProcessingError);
            Assert.Equal(RawTransactionProcessingStatus.Failed, db.RawAttendanceTransactions.Single(r => r.Id == broken).ProcessingStatus);
            Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.IntegrationChanged && a.Details!.Contains("1 punch(es)"));
        }

        Assert.Equal(new NormalizationResult(1, 0, 1, 0), await NormalizeAsync());
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal(RawTransactionProcessingStatus.Normalized, db.RawAttendanceTransactions.Single(r => r.Id == waiting).ProcessingStatus);
            Assert.Equal("Asia/Beirut", db.NormalizedAttendanceTransactions.Single().TimeZoneId);
            Assert.Null(await CreateIntegrationService(db).RetryFailedAsync(integrationId + 100, CancellationToken.None));
        }
    }

    [Fact]
    public async Task TheDeviceForUploads_TakesEveryPunch_UnknownOrRegisteredDeviceAlike_WithoutAFlag()
    {
        UseDeviceZone(NewYork);
        var uploadDeviceId = AddDeviceForUploads("Asia/Beirut");
        AddUnknownDeviceRaw("2026-10-05 08:00:00");
        AddRaw("2026-10-05 09:00:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(2, 0, 0, 0), result);
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var rows = db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).ToList();
        Assert.All(rows, n => Assert.Equal((uploadDeviceId, "Asia/Beirut", TransactionExceptionStatus.None), (n.DeviceId, n.TimeZoneId, n.ExceptionStatus)));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 5, 0, 0, TimeSpan.Zero), rows[0].EventTimestampUtc);

        // The registered device of the file is ignored, so its New York zone does not apply.
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 6, 0, 0, TimeSpan.Zero), rows[1].EventTimestampUtc);
    }

    [Fact]
    public async Task TheDeviceForUploads_IsIgnored_ByAnIntegrationThatDoesNotReceiveFiles()
    {
        Prepare();
        AddDeviceForUploads("Asia/Beirut");
        SetTransport(AttendanceTransport.RestApi);
        SetIntegrationZone("Asia/Beirut");
        AddUnknownDeviceRaw("2026-10-05 08:00:00");

        await NormalizeAsync();

        using var db = Database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal((null, TransactionExceptionStatus.FlaggedUnrecognizedDevice), (row.DeviceId, row.ExceptionStatus));
    }

    [Fact]
    public async Task RenormalizingFlaggedPunches_ReplacesThemWithPunchesOfTheDeviceForUploads_AndLeavesTheOldOnesAsHistory()
    {
        Prepare();
        SetIntegrationZone("Asia/Beirut");
        var rawId = AddUnknownDeviceRaw("2026-10-05 08:00:00");
        await NormalizeAsync();

        RenormalizeFlaggedResponse? queued;
        int integrationId;
        int uploadDeviceId;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            integrationId = db.AttendanceIntegrations.Single().Id;

            // Nothing can resolve the device yet, so nothing is taken up again, and the answer says why.
            var none = await CreateIntegrationService(db).RenormalizeFlaggedAsync(integrationId, CancellationToken.None);
            Assert.Equal((0, 1, false, "FileUpload"), (none!.Requeued, none.Unresolved, none.HasDeviceForUploads, none.Transport));
        }

        uploadDeviceId = AddDeviceForUploads("Asia/Beirut");
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            queued = await CreateIntegrationService(db).RenormalizeFlaggedAsync(integrationId, CancellationToken.None);
        }

        Assert.Equal((1, 0, true), (queued!.Requeued, queued.Unresolved, queued.HasDeviceForUploads));
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var old = db.NormalizedAttendanceTransactions.Single();
            Assert.Equal((NormalizedTransactionProcessingStatus.Excluded, TransactionExceptionStatus.FlaggedUnrecognizedDevice, (int?)null), (old.ProcessingStatus, old.ExceptionStatus, old.DeviceId));
            Assert.Equal(RawTransactionProcessingStatus.Pending, db.RawAttendanceTransactions.Single(r => r.Id == rawId).ProcessingStatus);
            Assert.Contains(db.AttendanceAuditLogs, a => a.Action == AttendanceAuditActions.IntegrationChanged && a.Details!.Contains("1 punch(es) flagged for an unrecognized device"));
        }

        Assert.Equal(new NormalizationResult(1, 0, 0, 0), await NormalizeAsync());
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var rows = db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal((NormalizedTransactionProcessingStatus.Excluded, TransactionExceptionStatus.FlaggedUnrecognizedDevice), (rows[0].ProcessingStatus, rows[0].ExceptionStatus));
            Assert.Equal((uploadDeviceId, TransactionExceptionStatus.None, DuplicateStatus.Unique, rawId), (rows[1].DeviceId, rows[1].ExceptionStatus, rows[1].DuplicateStatus, rows[1].RawTransactionId));
            Assert.Equal(RawTransactionProcessingStatus.Normalized, db.RawAttendanceTransactions.Single(r => r.Id == rawId).ProcessingStatus);

            // Taking the same flagged punches up again finds none: the old one is already replaced.
            var again = await CreateIntegrationService(db).RenormalizeFlaggedAsync(integrationId, CancellationToken.None);
            Assert.Equal((0, 0), (again!.Requeued, again.Unresolved));
            Assert.Null(await CreateIntegrationService(db).RenormalizeFlaggedAsync(integrationId + 100, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RenormalizingFlaggedPunches_AlsoTakesUpThoseWhoseDeviceWasRegisteredSince()
    {
        Prepare();
        SetIntegrationZone("Asia/Beirut");
        AddUnknownDeviceRaw("2026-10-05 08:00:00");
        await NormalizeAsync();

        // The file's device "99" is registered afterwards.
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var late = new AttendanceDevice { IntegrationId = db.AttendanceIntegrations.Single().Id, ExternalDeviceId = "99", Name = "Late", TimeZoneId = "Asia/Beirut" };
            db.AttendanceDevices.Add(late);
            db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { Device = late, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
            db.SaveChanges();
        }

        RenormalizeFlaggedResponse? queued;
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            queued = await CreateIntegrationService(db).RenormalizeFlaggedAsync(db.AttendanceIntegrations.Single().Id, CancellationToken.None);
        }

        Assert.Equal((1, 0, false), (queued!.Requeued, queued.Unresolved, queued.HasDeviceForUploads));
        await NormalizeAsync();
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            var live = db.NormalizedAttendanceTransactions.Single(n => n.ProcessingStatus != NormalizedTransactionProcessingStatus.Excluded);
            Assert.Equal(("Late", TransactionExceptionStatus.None), (db.AttendanceDevices.Single(d => d.Id == live.DeviceId).Name, live.ExceptionStatus));
        }
    }

    private static VendorTimestamp At(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0), null);

    private AttendanceIntegrationService CreateIntegrationService(RobeemDbContext db) =>
        new(db, new NoSync(), CreateAudit(db), new ServiceCollection().BuildServiceProvider(), new StubAdapterCatalog());

    private int AddDeviceForUploads(string timeZoneId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var device = new AttendanceDevice { IntegrationId = integration.Id, ExternalDeviceId = "upload", Name = "Uploads", TimeZoneId = timeZoneId };
        db.AttendanceDevices.Add(device);

        // Employees are mapped per device, so the device that takes the uploaded punches knows the file's employee id.
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { Device = device, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
        db.SaveChanges();
        integration.DefaultDeviceId = device.Id;
        db.SaveChanges();
        return device.Id;
    }

    private void SetTransport(AttendanceTransport transport)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var connection = db.AttendanceConnections.SingleOrDefault(c => c.IntegrationId == integration.Id);
        if (connection is null)
        {
            db.AttendanceConnections.Add(new AttendanceConnection { IntegrationId = integration.Id, Name = "Default", IsDefault = true, Transport = transport });
        }
        else
        {
            connection.Transport = transport;
        }

        db.SaveChanges();
    }

    private void SetIntegrationZone(string? timeZoneId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceIntegrations.Single().TimeZoneId = timeZoneId;
        db.SaveChanges();
    }

    private int AddUnknownDeviceRaw(string timestamp)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = db.AttendanceIntegrations.Single().Id,
            ImportBatchId = db.AttendanceProcessingBatches.Single().Id,
            DeviceExternalId = "99",
            ExternalEmployeeId = "E-1",
            TransactionTimestampRaw = timestamp,
            TransactionTypeRaw = "IN",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = Guid.NewGuid().ToString("N"),
        };
        db.RawAttendanceTransactions.Add(raw);
        db.SaveChanges();
        return raw.Id;
    }

    private int AddFailedRaw(string error)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = db.AttendanceIntegrations.Single().Id,
            ImportBatchId = db.AttendanceProcessingBatches.Single().Id,
            TransactionTimestampRaw = "x",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = Guid.NewGuid().ToString("N"),
            ProcessingStatus = RawTransactionProcessingStatus.Failed,
            ProcessingError = error,
        };
        db.RawAttendanceTransactions.Add(raw);
        db.SaveChanges();
        return raw.Id;
    }

    private void UseDeviceZone(string timeZoneId)
    {
        using (var db = Database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendanceDevices.OrderBy(d => d.Id).First().TimeZoneId = timeZoneId;
            db.SaveChanges();
        }

        Prepare();
    }

    private void Prepare()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);

        // The base fixture's seed row is not a punch of this test; normalization must leave it alone.
        db.RawAttendanceTransactions.Single().ProcessingStatus = RawTransactionProcessingStatus.Normalized;
        var integration = db.AttendanceIntegrations.Single();
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { DeviceId = db.AttendanceDevices.Where(d => d.IntegrationId == integration.Id).OrderBy(d => d.Id).First().Id, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
        db.SaveChanges();
    }

    private int AddRaw(string timestamp, string type)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var device = db.AttendanceDevices.OrderBy(d => d.Id).First();
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = integration.Id,
            ImportBatchId = db.AttendanceProcessingBatches.Single().Id,
            DeviceId = device.Id,
            DeviceExternalId = device.ExternalDeviceId,
            ExternalEmployeeId = "E-1",
            TransactionTimestampRaw = timestamp,
            TransactionTypeRaw = type,
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = Guid.NewGuid().ToString("N"),
        };
        db.RawAttendanceTransactions.Add(raw);
        db.SaveChanges();
        return raw.Id;
    }

    private async Task<NormalizationResult> NormalizeAsync()
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var batchId = db.AttendanceProcessingBatches.Single().Id;
        var service = new NormalizationService(db, new NormalizedTransactionRecorder(db), new AttendancePolicyResolver(db), _interpreters, NullLogger<NormalizationService>.Instance);
        return await service.NormalizeBatchAsync(batchId, CancellationToken.None);
    }

    private sealed class NoSync : IIntegrationSyncService
    {
        public Task<IReadOnlyList<PollingIntegration>> ListPollingIntegrationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> SyncAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcome> ReconcileAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> PruneEmptyBatchesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
