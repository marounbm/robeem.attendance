using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;
using robeem.Infrastructure.Persistence;

namespace robeem.Tests.Attendance;

public sealed class NormalizationServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _interpreters = new ServiceCollection()
        .AddSingleton<ICustomFormatInterpreterFactory, CustomFormatInterpreterFactory>()
        .BuildServiceProvider();

    private readonly int _employeeId;
    private readonly int _deviceId;
    private readonly int _integrationId;
    private readonly int _batchId;

    public NormalizationServiceTests()
    {
        _database.SeedAttendanceCatalog();
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            Connector = AttendanceTestData.Seeded(db, "generic-csv"),
            DisplayName = "HQ",
        };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "Asia/Beirut" };
        var batch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload };
        db.AddRange(device, batch, new ExternalEmployeeIdMapping { Device = device, ExternalEmployeeId = "EXT-1", Employee = employee });
        db.SaveChanges();

        _employeeId = employee.Id;
        _deviceId = device.Id;
        _integrationId = integration.Id;
        _batchId = batch.Id;
    }

    public void Dispose()
    {
        _interpreters.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task ResolvablePunch_IsNormalized_WithLocalTimeInTheDeviceZone()
    {
        var rawId = AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(1, 0, 0, 0), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Equal(rawId, row.RawTransactionId);
        Assert.Equal(_employeeId, row.EmployeeId);
        Assert.Equal(_deviceId, row.DeviceId);
        Assert.Equal(NormalizedEventType.CheckIn, row.NormalizedEventType);
        Assert.Equal(EventConfidenceStatus.Certain, row.ConfidenceStatus);
        Assert.Equal(TransactionExceptionStatus.None, row.ExceptionStatus);
        Assert.Equal(DuplicateStatus.Unique, row.DuplicateStatus);
        Assert.Equal("IN", row.OriginalEventType);

        // Beirut is UTC+3 in October; the default 120-second bucket floors 06:00:00 to itself.
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal(new DateTime(2026, 10, 4, 9, 0, 0), row.EventTimestampLocal);
        Assert.Equal(new DateTime(2026, 10, 4), row.AttendanceDate);
        Assert.Equal("Asia/Beirut", row.TimeZoneId);
        Assert.Equal(row.EventTimestampUtc, row.DuplicateBucketTimestampUtc);

        Assert.Equal(RawTransactionProcessingStatus.Normalized, db.RawAttendanceTransactions.Single().ProcessingStatus);
    }

    [Fact]
    public async Task UnresolvableEmployee_IsStillNormalized_AndFlagged()
    {
        AddRaw("EXT-404", "DEV-1", "2026-10-04 09:00:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(1, 0, 1, 0), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();
        Assert.Null(row.EmployeeId);
        Assert.Equal(_deviceId, row.DeviceId);
        Assert.Equal(TransactionExceptionStatus.FlaggedUnrecognizedEmployee, row.ExceptionStatus);
        Assert.Equal(RawTransactionProcessingStatus.Normalized, db.RawAttendanceTransactions.Single().ProcessingStatus);
    }

    [Fact]
    public async Task UnresolvableDevice_IsStillNormalized_AndFlagged_InUtc()
    {
        AddRaw("EXT-1", "DEV-9", "2026-10-04T08:58:00+03:00", "OUT");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(1, 0, 1, 0), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var row = db.NormalizedAttendanceTransactions.Single();

        // Employees are mapped per device, so a punch of a device nobody registered has no employee either.
        Assert.Null(row.EmployeeId);
        Assert.Null(row.DeviceId);
        Assert.Equal(TransactionExceptionStatus.FlaggedUnrecognizedDevice, row.ExceptionStatus);
        Assert.Equal(NormalizedEventType.CheckOut, row.NormalizedEventType);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 5, 58, 0, TimeSpan.Zero), row.EventTimestampUtc);
        Assert.Equal("UTC", row.TimeZoneId);
    }

    [Fact]
    public async Task TheSameVendorId_IsADifferentEmployeeOnEachDevice_AndAnUnmappedOneIsFlagged()
    {
        int otherEmployeeId;
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var other = new Profile { EmployeeNumber = "E-2", FirstName = "Grace", LastName = "Hopper", Title = "Grace Hopper" };
            var branch = new AttendanceDevice { IntegrationId = db.AttendanceIntegrations.Single().Id, ExternalDeviceId = "DEV-2", Name = "Branch door", TimeZoneId = "Asia/Beirut" };
            var unmapped = new AttendanceDevice { IntegrationId = db.AttendanceIntegrations.Single().Id, ExternalDeviceId = "DEV-3", Name = "Back door", TimeZoneId = "Asia/Beirut" };
            db.AddRange(other, branch, unmapped, new ExternalEmployeeIdMapping { Device = branch, ExternalEmployeeId = "EXT-1", Employee = other });
            db.SaveChanges();
            otherEmployeeId = other.Id;
        }

        AddRaw("EXT-1", "DEV-1", "2026-10-04T08:00:00+03:00", "IN");
        AddRaw("EXT-1", "DEV-2", "2026-10-04T08:30:00+03:00", "IN");
        AddRaw("EXT-1", "DEV-3", "2026-10-04T09:00:00+03:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(3, 0, 1, 0), result);
        using var check = _database.CreateContext(TestDatabase.TenantA);
        var rows = check.NormalizedAttendanceTransactions.OrderBy(n => n.RawTransactionId).ToList();
        Assert.Equal([_employeeId, otherEmployeeId, (int?)null], rows.Select(n => n.EmployeeId));
        Assert.Equal([TransactionExceptionStatus.None, TransactionExceptionStatus.None, TransactionExceptionStatus.FlaggedUnrecognizedEmployee], rows.Select(n => n.ExceptionStatus));
    }

    [Fact]
    public async Task InvalidTimestamp_ProducesNoNormalizedRow_AndFailsTheRawRow()
    {
        AddRaw("EXT-1", "DEV-1", "04/10/2026 nine-ish", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(0, 0, 0, 1), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.NormalizedAttendanceTransactions);
        var raw = db.RawAttendanceTransactions.Single();
        Assert.Equal(RawTransactionProcessingStatus.Failed, raw.ProcessingStatus);
        Assert.Contains("Unparseable timestamp", raw.ProcessingError);
    }

    [Fact]
    public async Task DuplicatePair_GivesOneUniqueAndOneDuplicateConfirmed()
    {
        // 06:00:10 and 06:01:00 UTC share the 06:00:00 bucket of the default 120-second window.
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:10", "IN");
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:01:00", "Check-In");

        var result = await NormalizeAsync();

        Assert.Equal(new NormalizationResult(2, 1, 0, 0), result);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var rows = db.NormalizedAttendanceTransactions.OrderBy(n => n.Id).ToList();
        Assert.Equal(DuplicateStatus.Unique, rows[0].DuplicateStatus);
        Assert.Equal(DuplicateStatus.DuplicateConfirmed, rows[1].DuplicateStatus);
        Assert.Equal(rows[0].Id, rows[1].DuplicateOfTransactionId);
        Assert.All(db.RawAttendanceTransactions, r => Assert.Equal(RawTransactionProcessingStatus.Normalized, r.ProcessingStatus));
    }

    [Fact]
    public async Task DuplicateWindow_ComesFromTheEmployeesSchedulePolicy()
    {
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            var schedule = new TimeSchedule { Title = "Week" };
            var team = new TimeScheduleTeam { TimeSchedule = schedule, Title = "All" };
            db.AddRange(
                team,
                new AttendancePolicy { Title = "Default", DuplicateTimeWindowSeconds = 60 },
                new AttendancePolicy { Title = "Week", TimeSchedule = schedule, DuplicateTimeWindowSeconds = 600 });
            db.SaveChanges();
            db.TimeScheduleEmployees.Add(new TimeScheduleEmployee { TimeScheduleId = schedule.Id, TeamId = team.Id, EmployeeId = _employeeId });
            db.SaveChanges();
        }

        // Five minutes apart: duplicates under the schedule's 10-minute window, not the default 1-minute one.
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:00", "IN");
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:05:00", "IN");

        var result = await NormalizeAsync();

        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public async Task UnlabeledPunch_FailsWhenThePolicyDoesNotInterpret_AndIsKeptAsAmbiguousWhenItDoes()
    {
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:00", null);
        Assert.Equal(new NormalizationResult(0, 0, 0, 1), await NormalizeAsync());

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            db.AttendancePolicies.Add(new AttendancePolicy { Title = "Default", UnlabeledPunchInterpretationMode = UnlabeledPunchInterpretationMode.Sequential });
            db.SaveChanges();
        }

        AddRaw("EXT-1", "DEV-1", "2026-10-04 17:00:00", null);
        Assert.Equal(new NormalizationResult(1, 0, 0, 0), await NormalizeAsync());

        using var check = _database.CreateContext(TestDatabase.TenantA);
        var row = check.NormalizedAttendanceTransactions.Single();
        Assert.Equal(NormalizedEventType.Unknown, row.NormalizedEventType);
        Assert.Equal(EventConfidenceStatus.Ambiguous, row.ConfidenceStatus);
    }

    [Fact]
    public async Task UnrecognizedEventType_FailsTheRawRow()
    {
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:00", "LUNCH?");

        Assert.Equal(new NormalizationResult(0, 0, 0, 1), await NormalizeAsync());

        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Contains("Unrecognized event type 'LUNCH?'", db.RawAttendanceTransactions.Single().ProcessingError);
    }

    [Fact]
    public async Task PendingBatches_AreListed_UntilNormalized()
    {
        AddRaw("EXT-1", "DEV-1", "2026-10-04 09:00:00", "IN");

        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.Equal([_batchId], await CreateService(db).ListBatchesWithPendingTransactionsAsync(CancellationToken.None));
        }

        await NormalizeAsync();

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(await CreateService(check).ListBatchesWithPendingTransactionsAsync(CancellationToken.None));
    }

    private async Task<NormalizationResult> NormalizeAsync()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await CreateService(db).NormalizeBatchAsync(_batchId, CancellationToken.None);
    }

    private NormalizationService CreateService(RobeemDbContext db) => new(
        db,
        new NormalizedTransactionRecorder(db),
        new AttendancePolicyResolver(db),
        _interpreters,
        NullLogger<NormalizationService>.Instance);

    private int AddRaw(string externalEmployeeId, string deviceExternalId, string timestamp, string? type)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var raw = new RawAttendanceTransaction
        {
            IntegrationId = _integrationId,
            ImportBatchId = _batchId,
            ExternalEmployeeId = externalEmployeeId,
            DeviceExternalId = deviceExternalId,
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
}
