using robeem.Application.Attendance;
using robeem.Application.Common;
using robeem.Contracts.Attendance;
using robeem.DataModel;

namespace robeem.Tests.Attendance;

/// <summary>Pay periods (screens phase 3): creating, closing and reopening, and what stays on record.</summary>
public sealed class PayPeriodServiceTests : IDisposable
{
    private static readonly DateTime October = new(2026, 10, 1);

    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task APeriod_IsCreatedOpen_WithDatesOnly_AndAudited()
    {
        var result = await CreateAsync(October.AddHours(13), October.AddDays(30).AddHours(9));

        Assert.True(result.Succeeded);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        var period = db.PayPeriods.Single();
        Assert.Equal((October, new DateTime(2026, 10, 31), false), (period.StartDate, period.EndDate, period.IsClosed));
        var entry = Assert.Single(db.AttendanceAuditLogs);
        Assert.Equal((AttendanceAuditActions.PayPeriodChanged, "employee:7", period.Id), (entry.Action, entry.Actor, entry.TargetId));
        Assert.Contains("Created 2026-10-01 to 2026-10-31", entry.Details);
    }

    [Fact]
    public async Task APeriodOfOneDay_AndOneOfAYear_AreAllowed_AYearAndADay_IsNot()
    {
        Assert.True((await CreateAsync(October, October)).Succeeded);
        Assert.True((await CreateAsync(new DateTime(2027, 1, 1), new DateTime(2027, 12, 31).AddDays(0))).Succeeded);

        var tooLong = await CreateAsync(new DateTime(2030, 1, 1), new DateTime(2031, 1, 2));

        Assert.Contains("at most 366 days", tooLong.Errors[nameof(PayPeriodInput.EndDate)]);
    }

    [Fact]
    public async Task ABackwardsPeriod_IsRefused()
    {
        var result = await CreateAsync(October.AddDays(5), October);

        Assert.Contains("ends before it starts", result.Errors[nameof(PayPeriodInput.EndDate)]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.Empty(db.PayPeriods);
    }

    [Theory]
    [InlineData(1, 31, true)]
    [InlineData(20, 40, true)]
    [InlineData(-10, 1, true)]
    [InlineData(5, 10, true)]
    [InlineData(-10, 40, true)]
    [InlineData(31, 60, false)]
    [InlineData(-30, 0, false)]
    public async Task APeriod_MayNotOverlapAnother_ButMayTouchItsNeighbours(int startOffset, int endOffset, bool overlaps)
    {
        // The existing period is 2026-10-02 to 2026-10-31 (offsets 1 to 30 from the 1st).
        await CreateAsync(October.AddDays(1), October.AddDays(30));

        var result = await CreateAsync(October.AddDays(startOffset), October.AddDays(endOffset));

        Assert.Equal(overlaps, !result.Succeeded);
        if (overlaps)
        {
            Assert.Contains("overlaps another", result.Errors[nameof(PayPeriodInput.StartDate)]);
        }
    }

    [Fact]
    public async Task APeriod_IsClosedOnce_AndAnAlreadyClosedOneIsRefused()
    {
        var id = (await CreateAsync(October, October.AddDays(30))).Id!.Value;

        Assert.True((await CloseAsync(id)).Succeeded);
        var again = await CloseAsync(id);

        Assert.Contains("already closed", again.Errors["id"]);
        using var db = _database.CreateContext(TestDatabase.TenantA);
        Assert.True(db.PayPeriods.Single().IsClosed);
        Assert.Contains(db.AttendanceAuditLogs.ToList(), a => a.Details!.StartsWith("Closed 2026-10-01"));
    }

    [Fact]
    public async Task ReopeningNeedsAReason_AndTheReasonIsOnRecord()
    {
        var id = (await CreateAsync(October, October.AddDays(30))).Id!.Value;
        await CloseAsync(id);

        var withoutReason = await ReopenAsync(id, "   ");
        var tooLong = await ReopenAsync(id, new string('x', 1001));
        Assert.Contains("reason is required", withoutReason.Errors[nameof(ReopenPayPeriodRequest.Reason)]);
        Assert.Contains("at most 1000", tooLong.Errors[nameof(ReopenPayPeriodRequest.Reason)]);
        using (var db = _database.CreateContext(TestDatabase.TenantA))
        {
            Assert.True(db.PayPeriods.Single().IsClosed);
        }

        Assert.True((await ReopenAsync(id, "  A late timesheet arrived.  ")).Succeeded);

        using var check = _database.CreateContext(TestDatabase.TenantA);
        Assert.False(check.PayPeriods.Single().IsClosed);
        Assert.Contains(check.AttendanceAuditLogs.ToList(), a => a.Details!.EndsWith("Reason: A late timesheet arrived."));
    }

    [Fact]
    public async Task AnOpenPeriod_CanNotBeReopened_AndAnUnknownOneIsNotFound()
    {
        var id = (await CreateAsync(October, October.AddDays(30))).Id!.Value;

        Assert.Contains("not closed", (await ReopenAsync(id, "why")).Errors["id"]);
        Assert.True((await CloseAsync(999)).NotFound);
        Assert.True((await ReopenAsync(999, "why")).NotFound);
    }

    [Fact]
    public async Task ThePeriods_AreListedLatestFirst_AndPerTenant()
    {
        await CreateAsync(October, October.AddDays(30));
        await CreateAsync(October.AddMonths(1), October.AddMonths(1).AddDays(29));
        await CreateAsync(October.AddMonths(-1), October.AddDays(-1));

        using var db = _database.CreateContext(TestDatabase.TenantA);
        var list = await new PayPeriodService(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System)).ListAsync(CancellationToken.None);
        Assert.Equal([new DateTime(2026, 11, 1), October, new DateTime(2026, 9, 1)], list.Select(p => p.StartDate));

        using var other = _database.CreateContext(TestDatabase.TenantB);
        Assert.Empty(await new PayPeriodService(other, new AttendanceAuditTrail(other, new TestUser(7), TimeProvider.System)).ListAsync(CancellationToken.None));
    }

    private async Task<SaveResult> CreateAsync(DateTime start, DateTime end)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).CreateAsync(new PayPeriodInput { StartDate = start, EndDate = end }, CancellationToken.None);
    }

    private async Task<SaveResult> CloseAsync(int id)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).CloseAsync(id, CancellationToken.None);
    }

    private async Task<SaveResult> ReopenAsync(int id, string reason)
    {
        using var db = _database.CreateContext(TestDatabase.TenantA);
        return await Service(db).ReopenAsync(id, reason, CancellationToken.None);
    }

    private PayPeriodService Service(robeem.Infrastructure.Persistence.RobeemDbContext db) =>
        new(db, new AttendanceAuditTrail(db, new TestUser(7), TimeProvider.System));
}
