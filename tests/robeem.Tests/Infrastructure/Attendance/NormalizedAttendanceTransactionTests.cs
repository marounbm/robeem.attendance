using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using robeem.Application.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Infrastructure.Attendance;

public sealed class NormalizedAttendanceTransactionTests : IDisposable
{
    private static readonly DateTimeOffset Bucket = new(2026, 10, 4, 6, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _database = new();
    private readonly Fixture _fixture;

    public NormalizedAttendanceTransactionTests()
    {
        _fixture = SeedFixture();
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Record_OfANewKey_IsUnique()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);

        var row = await new NormalizedTransactionRecorder(db).RecordAsync(NewCheckIn(Bucket.AddSeconds(5)));

        Assert.Equal(DuplicateStatus.Unique, row.DuplicateStatus);
        Assert.Null(row.DuplicateOfTransactionId);
        Assert.Equal(TestDatabase.TenantA, db.NormalizedAttendanceTransactions.AsNoTracking().Single().TenantId);
    }

    [Fact]
    public async Task Record_OfTheSameKeyTwice_StoresOneUniqueAndOneDuplicateConfirmed()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var recorder = new NormalizedTransactionRecorder(db);

        var first = await recorder.RecordAsync(NewCheckIn(Bucket.AddSeconds(5)));
        var second = await recorder.RecordAsync(NewCheckIn(Bucket.AddSeconds(40)));

        Assert.Equal(DuplicateStatus.Unique, first.DuplicateStatus);
        Assert.Equal(DuplicateStatus.DuplicateConfirmed, second.DuplicateStatus);
        Assert.Equal(first.Id, second.DuplicateOfTransactionId);
        Assert.Equal(2, db.NormalizedAttendanceTransactions.Count());
    }

    [Fact]
    public async Task Record_OfADifferentEventType_IsUnique()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var recorder = new NormalizedTransactionRecorder(db);
        await recorder.RecordAsync(NewCheckIn(Bucket));

        var checkOut = NewCheckIn(Bucket);
        checkOut.NormalizedEventType = NormalizedEventType.CheckOut;

        Assert.Equal(DuplicateStatus.Unique, (await recorder.RecordAsync(checkOut)).DuplicateStatus);
    }

    [Fact]
    public async Task Record_ThatLosesAConcurrentRace_BecomesDuplicateConfirmed()
    {
        // A concurrent writer stores the same key after this recorder checked for it, just before
        // it saves, so only the unique index notices.
        var competitor = new SaveOnceInterceptor(() =>
        {
            using var other = _database.CreateContext(TestDatabase.TenantA);
            var winner = NewCheckIn(Bucket);
            other.NormalizedAttendanceTransactions.Add(winner);
            other.SaveChanges();
            return winner.Id;
        });
        using var db = _database.CreateContext(TestDatabase.TenantA, competitor);

        var row = await new NormalizedTransactionRecorder(db).RecordAsync(NewCheckIn(Bucket.AddSeconds(30)));

        Assert.Equal(DuplicateStatus.DuplicateConfirmed, row.DuplicateStatus);
        Assert.Equal(competitor.WinnerId, row.DuplicateOfTransactionId);
        Assert.Equal(2, db.NormalizedAttendanceTransactions.Count());
    }

    [Fact]
    public async Task Record_WithoutAnEmployee_IsNeverMarkedDuplicate()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var recorder = new NormalizedTransactionRecorder(db);

        var first = NewCheckIn(Bucket);
        first.EmployeeId = null;
        var second = NewCheckIn(Bucket);
        second.EmployeeId = null;
        await recorder.RecordAsync(first);

        Assert.Equal(DuplicateStatus.Unique, (await recorder.RecordAsync(second)).DuplicateStatus);
    }

    private NormalizedAttendanceTransaction NewCheckIn(DateTimeOffset utc) => new()
    {
        EmployeeId = _fixture.EmployeeId,
        DeviceId = _fixture.DeviceId,
        IntegrationId = _fixture.IntegrationId,
        RawTransactionId = _fixture.RawTransactionId,
        AttendanceDate = utc.DateTime.Date,
        EventTimestampUtc = utc,
        EventTimestampLocal = utc.DateTime.AddHours(3),
        TimeZoneId = "Asia/Beirut",
        NormalizedEventType = NormalizedEventType.CheckIn,
        DuplicateBucketTimestampUtc = Bucket,
    };

    private Fixture SeedFixture()
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var employee = new Profile { EmployeeNumber = "E-1", FirstName = "Ada", LastName = "Lovelace", Title = "Ada Lovelace" };
        var integration = new AttendanceIntegration
        {
            Provider = new AttendanceProvider { ProviderKey = "generic-csv", DisplayName = "Generic CSV" },
            DisplayName = "HQ",
        };
        var device = new AttendanceDevice { Integration = integration, ExternalDeviceId = "DEV-1", Name = "Front door", TimeZoneId = "Asia/Beirut" };
        var raw = new RawAttendanceTransaction
        {
            Integration = integration,
            ImportBatch = new AttendanceProcessingBatch { Integration = integration, BatchType = AttendanceBatchType.ManualUpload },
            TransactionTimestampRaw = "2026-10-04 09:00",
            RawPayload = "{}",
            SourceSystem = "generic-csv",
            RecordHash = "hash",
        };
        db.AddRange(employee, device, raw);
        db.SaveChanges();
        return new Fixture(employee.Id, device.Id, integration.Id, raw.Id);
    }

    private sealed record Fixture(int EmployeeId, int DeviceId, int IntegrationId, int RawTransactionId);

    private sealed class SaveOnceInterceptor : SaveChangesInterceptor
    {
        private readonly Func<int> _beforeFirstSave;

        public SaveOnceInterceptor(Func<int> beforeFirstSave)
        {
            _beforeFirstSave = beforeFirstSave;
        }

        public int? WinnerId { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            WinnerId ??= _beforeFirstSave();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
