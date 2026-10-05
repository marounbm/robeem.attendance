using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using robeem.Application.Attendance;
using robeem.DataModel;
using robeem.Infrastructure.Attendance.Providers;

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
        .AddKeyedSingleton<IAttendanceEventInterpreter, GenericCsvInterpreter>(GenericCsvProvider.ProviderKey)
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

    private static VendorTimestamp At(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0), null);

    private void UseDeviceZone(string timeZoneId)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        db.AttendanceDevices.Single().TimeZoneId = timeZoneId;

        // The base fixture's seed row is not a punch of this test; normalization must leave it alone.
        db.RawAttendanceTransactions.Single().ProcessingStatus = RawTransactionProcessingStatus.Normalized;
        var integration = db.AttendanceIntegrations.Single();
        db.ExternalEmployeeIdMappings.Add(new ExternalEmployeeIdMapping { IntegrationId = integration.Id, ExternalEmployeeId = "E-1", EmployeeId = EmployeeId });
        db.SaveChanges();
    }

    private int AddRaw(string timestamp, string type)
    {
        using var db = Database.CreateContext(TestDatabase.TenantA);
        var integration = db.AttendanceIntegrations.Single();
        var device = db.AttendanceDevices.Single();
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
            SourceSystem = GenericCsvProvider.ProviderKey,
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
}
